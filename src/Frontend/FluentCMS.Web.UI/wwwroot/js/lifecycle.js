let events = {}
export const lifecycle = {
    on(event, callback) {
        console.log('register lifecycle callback', event)
        events[event] ??= []
        events[event].push(callback)
    },
    trigger(event, detail) {
        console.log('trigger lifecycle callback', event)
        for(let callback of events[event] ?? []) {
            console.log(callback)
            callback(detail)
        }
    },
    off(event, callback) {
        console.log('unregister lifecycle callback', event)

        if(events[event]) {
            events[event] = events[event].filter(x => x !== callback)
        }
    }
}