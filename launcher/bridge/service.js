/* ============================================================
   bridge/service.js — private bridge between the launcher and Vetro.

   Vetro (http://127.0.0.1:8099) does not let the screen's apps read
   its answers (CORS). This service (same package as the launcher, started
   by it when needed and when the screen starts) makes the request on its behalf
   and sends the answer back. At start-up, it also opens the launcher.

   Security: NO network port is opened. The launcher and this service
   talk through trusted Tizen "message ports": only apps
   signed with the same certificate as this package can use them.
   Only the paths the launcher needs are relayed. The service
   stops on its own after 15 minutes without a request.

   Tizen 9: services go through a router (service_runner.js) that
   crashes if the app attaches a trusted reply port to its message.
   So the launcher sends its request with no port attached, and this service answers
   through its own trusted port to APL_BRIDGE_REPLY. Large
   answers are sent in several pieces (message port limit).
   ============================================================ */
"use strict";

var http = require("http");
var fs = require("fs");

var PORT_NAME = "APL_BRIDGE";
var APP_ID = "APLauncher.Home", REPLY_PORT = "APL_BRIDGE_REPLY";
/* Tizen 9 refuses 127.0.0.1 to services (EHOSTUNREACH), but not the same local address written in IPv6. */
var HOSTS = ["::ffff:127.0.0.1", "127.0.0.1"], VETRO_PORT = 8099, hostIndex = 0;
var ALLOWED = /^\/(status|screen|tv-settings|tv-menu|exec|components\/action|control|apps)(\/|\?|$)/;
var IDLE_MS = 15 * 60 * 1000;
var CHUNK = 4000;                       // characters per message (at most 12 KB once encoded)
var BOOT_WINDOW_S = 240, LAUNCH_TRIES = 5;

var port = null, idle = null, openedAtBoot = false;

/* A log of what the service does, for a screen whose system log cannot be read: /tmp (any root
   shell, through Vetro) and the package's data folder, next to the Overlay's overlay.log. */
var LOG_FILES = ["/tmp/apl-bridge.log", "/opt/usr/home/owner/apps_rw/APLauncher/data/bridge.log"];
function note(message) {
    var line = new Date().toISOString().substr(11, 12) + " " + message + "\n";
    LOG_FILES.forEach(function (f) {
        try {
            try { if (fs.statSync(f).size > 32768) fs.unlinkSync(f); } catch (e) {}
            fs.appendFileSync(f, line);
        } catch (e) {}
    });
}
note("loaded: node " + process.version + ", tizen " + (typeof tizen) + ", uptime " + (function () {
    try { return fs.readFileSync("/proc/uptime", "utf8").split(" ")[0]; } catch (e) { return "?"; }
})() + " s");
process.on("uncaughtException", function (e) { note("uncaught: " + (e && e.stack || e)); });
process.on("exit", function (code) { note("exit " + code); });

function stopLater() {
    clearTimeout(idle);
    idle = setTimeout(function () {
        try { tizen.application.getCurrentApplication().exit(); } catch (e) {}
    }, IDLE_MS);
}

function field(data, key) {
    for (var i = 0; i < data.length; i++) if (data[i].key === key) return String(data[i].value);
    return "";
}

function answer(id, status, body) {
    var back;
    try { back = tizen.messageport.requestTrustedRemoteMessagePort(APP_ID, REPLY_PORT); } catch (e) { return; }
    body = String(body || "");
    var parts = Math.max(1, Math.ceil(body.length / CHUNK));
    for (var i = 0; i < parts; i++) {
        try {
            back.sendMessage([{ key: "id", value: id }, { key: "status", value: String(status) },
                              { key: "part", value: String(i) }, { key: "parts", value: String(parts) },
                              { key: "body", value: body.substr(i * CHUNK, CHUNK) }]);
        } catch (e) {}
    }
}

function onMessage(data) {
    stopLater();
    note("request " + field(data, "method") + " " + field(data, "path"));
    var id = field(data, "id"), method = field(data, "method") || "GET", path = field(data, "path"), body = field(data, "body");
    if (!ALLOWED.test(path) || (method !== "GET" && method !== "POST")) {
        answer(id, 404, '{"error":"not relayed"}');
        return;
    }
    relay(id, method, path, Buffer.from(body || "", "utf8"), HOSTS.length - 1);
}

function relay(id, method, path, buf, retries) {
    var req = http.request({
        host: HOSTS[hostIndex], port: VETRO_PORT, path: path, method: method,
        headers: { "Content-Type": "application/json", "Content-Length": buf.length }
    }, function (res) {
        var chunks = [];
        res.on("data", function (c) { chunks.push(c); });
        res.on("end", function () { answer(id, res.statusCode || 502, Buffer.concat(chunks).toString("utf8")); });
    });
    req.on("error", function (e) {
        // Connection refused: the request did not go out, try the other way of writing the address
        if (retries > 0 && /EHOSTUNREACH|EACCES|ECONNREFUSED|ENETUNREACH|EINVAL/.test(e.code)) {
            hostIndex = (hostIndex + 1) % HOSTS.length;
            relay(id, method, path, buf, retries - 1);
            return;
        }
        note("Vetro unreachable: " + e.code + " " + e.message);
        answer(id, 502, JSON.stringify({ ok: false, error: "Vetro unreachable: " + e.message }));
    });
    req.setTimeout(150000, function () { req.abort(); });
    req.end(buf);
}

function start() {
    stopLater();
    if (port) return;
    port = tizen.messageport.requestTrustedLocalMessagePort(PORT_NAME);
    port.addMessagePortListener(onMessage);
    note("listening on " + PORT_NAME);
}

/* Full start-up (power cut, restart): the screen comes back on its last input and,
   with no device plugged in, its HDMI page puts it to sleep at about 78 s. This service starts with the screen,
   at about 57 s (on-boot, config.xml): opening the launcher cancels the countdown. Started later
   by the launcher, it does nothing. */
function isBooting() {
    try { return parseFloat(fs.readFileSync("/proc/uptime", "utf8")) < BOOT_WINDOW_S; } catch (e) { return false; }
}
function openLauncher(tries) {
    if (openedAtBoot || tries <= 0 || !isBooting()) return;
    function again() { setTimeout(function () { openLauncher(tries - 1); }, 3000); }
    try {
        tizen.application.getAppsContext(function (running) {
            if (running.some(function (c) { return c.appId === APP_ID; })) { openedAtBoot = true; return; }
            tizen.application.launch(APP_ID, function () { openedAtBoot = true; }, again);
        }, again);
    } catch (e) { again(); }
}

function begin(from) {
    note(from);
    try { start(); } catch (e) { note("start failed: " + (e && (e.name + ": " + e.message) || e)); }
    try { openLauncher(LAUNCH_TRIES); } catch (e) { note("openLauncher failed: " + (e && e.message || e)); }
}
module.exports.onStart = function () { begin("onStart"); };
// Some environments run the script without calling onStart
try { begin("script start"); } catch (e) {}
module.exports.onRequest = function () { note("onRequest"); try { start(); } catch (e) { note("start failed: " + (e && e.message || e)); } };
module.exports.onExit = function () { note("onExit"); clearTimeout(idle); port = null; };
