// The admin's tabs in this browser answer for the session each holds, by the tab id kept beside it in sessionStorage. A tab
// duplicated from another, or opened by it, starts with a copy of that sessionStorage, and asks before it uses the session.
// The channel only tells a copy from a reload: it carries tab ids, never a token or another secret.
// Without BroadcastChannel no tab answers, and a copy keeps the session it copied, as before.
const channel = typeof BroadcastChannel === "function" ? new BroadcastChannel("fluentcms-admin/tabs") : null;
let held = null;

channel?.addEventListener("message", event => {
    if (held !== null && event.data?.ask === held) {
        channel.postMessage({ holds: held });
    }
});

export function hold(tabId) {
    held = tabId;
}

export function isHeldElsewhere(tabId, wait) {
    if (channel === null) {
        return Promise.resolve(false);
    }

    return new Promise(resolve => {
        const answered = event => {
            if (event.data?.holds === tabId) {
                done(true);
            }
        };
        const timer = setTimeout(() => done(false), wait);
        function done(result) {
            clearTimeout(timer);
            channel.removeEventListener("message", answered);
            resolve(result);
        }

        channel.addEventListener("message", answered);
        channel.postMessage({ ask: tabId });
    });
}
