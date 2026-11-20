// The admin's side of the preview frame's postMessage contract (spec 4.12). The checks of a message are the app's; this
// hands on only what the window of the pane's frame posted, as JSON, and posts the app's messages to the origin it names
// alone. The frame is found in the pane each time, since a reload puts a new one in its place.

function frameOf(pane) {
    return pane.querySelector("iframe")?.contentWindow ?? null;
}

export function listen(pane, target) {
    const receive = event => {
        if (event.source === null || event.source !== frameOf(pane)) {
            return;
        }

        let message;
        try {
            message = JSON.stringify(event.data);
        } catch {
            return;
        }

        if (typeof message === "string") {
            // The app may be gone, or the message refused there; neither is the page's to see.
            target.invokeMethodAsync("ReceiveAsync", event.origin, message).catch(() => { });
        }
    };
    window.addEventListener("message", receive);
    return { dispose: () => window.removeEventListener("message", receive) };
}

export function post(pane, origin, message) {
    frameOf(pane)?.postMessage(JSON.parse(message), origin);
}
