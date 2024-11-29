export function debounce(cb, timeout = 300) {
    let timer;
    return (...args) => {
        if(timer) clearTimeout(timer)
        timer = setTimeout(() => {
            cb(...args)
        }, timeout)
    }
}