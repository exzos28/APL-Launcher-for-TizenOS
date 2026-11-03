/* ============================================================
   overlay.js — the link with APL Overlay (overlay/ folder), the native app
   that shows Control Center on top of a video.

   The Home, Pause, Samsung TV Plus and Prime Video keys go to the overlay
   (Vetro rules): see overlay/Program.cs. The launcher
   starts it hidden so that it responds right away, and passes it the theme,
   the "Return to the Launcher" setting and the delay for closing background apps.

   With no overlay installed, the Home key must stay with the launcher and the others
   with Samsung: the Vetro rules are put back at start-up, so that no key is lost.
   ============================================================ */
(function (global) {
    "use strict";

    var APL = global.APL, sys = APL.sys;
    var OVERLAY = "APLauncher.Overlay", LAUNCHER = "APLauncher.Home", HOME_RULE = "physical_63";
    var installed = false, light = false;

    /* The apps the Echo can open: "Name=app:id", Home Screen first (the first ones
       are used the most). No ";" or "=" in a name, and few enough apps to stay readable
       in the Alexa app. */
    var ALEXA_APPS = 16;
    var IDLE_MINUTES = 10;          // an app in the background for this long is closed
    function alexaList() {
        return ((APL.allApps && APL.allApps()) || []).filter(function (a) {
            return !a.folder && a.id !== "apl.settings" && a.id !== "apl.sources";
        }).slice(0, ALEXA_APPS).map(function (a) {
            return String(a.name).replace(/[;=]/g, " ") + "=app:" + a.id;
        }).join(";");
    }

    /* The Home Screen apps, which the overlay closes when they stay in the background (overlay/IdleApps.cs):
       never a service. A virtual tile counts as the app it opens (TizenTube: TizenBrew). */
    function homeApps() {
        return ((APL.allApps && APL.allApps()) || []).map(function (a) {
            return a.folder ? "" : a.virtual ? (a.needs || "") : a.id;
        }).filter(function (id) { return /^[A-Za-z0-9_.-]+$/.test(id); }).join(";");
    }

    function send() {
        if (!installed || sys.PREVIEW) return;
        try {
            var T = global.tizen;
            var data = [new T.ApplicationControlData("apl_action", ["stay"]),
                        new T.ApplicationControlData("apl_theme", [light ? "light" : "dark"]),
                        new T.ApplicationControlData("apl_return", [sys.pref("homeReturn") ? "1" : "0"]),
                        new T.ApplicationControlData("apl_alexa_on", [sys.pref("alexaBridge") ? "1" : "0"]),
                        new T.ApplicationControlData("apl_alexa", [alexaList()]),
                        new T.ApplicationControlData("apl_idle", [sys.pref("idleClose") ? String(IDLE_MINUTES) : "0"]),
                        new T.ApplicationControlData("apl_apps", [homeApps()]),
                        new T.ApplicationControlData("apl_lang", [APL.lang || "en"])];
            var ctl = new T.ApplicationControl("http://tizen.org/appcontrol/operation/default", null, null, null, data);
            T.application.launchAppControl(ctl, OVERLAY, function () {}, function () {});
        } catch (e) {}
    }

    /* The Smart Hub tile opens Samsung's home on purpose: the overlay leaves it on screen until
       something else comes in front (overlay/Program.cs, apl_action samsung_home). */
    function passSamsungHome() {
        if (!installed || sys.PREVIEW) return;
        try {
            var T = global.tizen;
            var ctl = new T.ApplicationControl("http://tizen.org/appcontrol/operation/default", null, null, null,
                                               [new T.ApplicationControlData("apl_action", ["samsung_home"])]);
            T.application.launchAppControl(ctl, OVERLAY, function () {}, function () {});
        } catch (e) {}
    }

    /* Keys handed to the overlay while it is not there: Home goes back to the launcher, the others to Samsung. */
    function checkHomeRule() {
        sys.vetroGet("/remap").then(function (r) {
            var rules = (r && r.rules) || [], changed = false;
            rules = rules.filter(function (rule) {
                var mine = (rule.actions || []).filter(function (a) { return a.type === "launch" && a.appId === OVERLAY; });
                if (!mine.length) return true;
                changed = true;
                mine.forEach(function (a) { a.appId = LAUNCHER; });
                return rule.id === HOME_RULE;
            });
            if (changed) sys.vetroPost("/remap", { rules: rules }).catch(function () {});
        }, function () {});
    }

    function start() {
        if (sys.PREVIEW) return;
        APL.isInstalled(OVERLAY).then(function (yes) {
            installed = yes;
            if (yes) send(); else checkHomeRule();
        });
    }

    sys.onPref("homeReturn", send);
    sys.onPref("alexaBridge", send);
    sys.onPref("idleClose", send);

    APL.overlay = {
        start: start,
        setTheme: function (isLight) {
            if (isLight === light) return;
            light = isLight;
            send();
        },
        refresh: send,
        passSamsungHome: passSamsungHome,
        isInstalled: function () { return installed; }
    };
})(window);
