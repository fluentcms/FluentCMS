const headers = new Map();

const scrollingElement = document.getElementsByTagName('main')[0]

function debounce(cb, timeout = 300) {
    let timer;

    return (...args) => {
        if(timer) clearTimeout(timer)

        timer = setTimeout(() => {
            cb(...args)
        }, timeout)
    }
}

export function initialize(dotnet, element, config) {
    dispose(dotnet, element);

    const onScroll = debounce((event) => {
        var currentScrollTop = event.target.scrollTop ?? 0

        if (currentScrollTop <= 0) {
            element.classList.remove('f-page-header-pinned')
        } else {
            element.classList.add('f-page-header-pinned')
        }
    }, 20)
    
    scrollingElement.addEventListener('scroll', onScroll)

    headers.set(element, element);
}

export function dispose(dotnet, element) {
    const event = headers.get(element);

    if(!event) return;

    scrollingElement.removeEventListener('scroll', event)
    
    headers.delete(element);
}

