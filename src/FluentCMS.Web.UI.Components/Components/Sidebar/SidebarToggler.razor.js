const sidebars = new Map();

export function toggle(dotnet, id) {
    const sidebar = sidebars.get(id)

    sidebar.toggle()
}

export function initialize(dotnet, id) {
    const sidebar = document.getElementById(id)
    const backdrop = sidebar.nextElementSibling

    const hide = () => {
        sidebar.classList.add('-translate-x-full');
        backdrop.classList.add('hidden');
    }
    
    const cleanup = () => {
        sidebar.removeEventListener('click', hide); 
        backdrop.removeEventListener('click', hide); 
    }

    const toggle = () => {
        if(!sidebar || !backdrop) return;

        sidebar.classList.toggle('-translate-x-full');
        backdrop.classList.toggle('hidden');
    }

    const init = () => {
        sidebar.addEventListener('click', hide); 
        backdrop.addEventListener('click', hide); 
    }

    init()

    sidebars.set(id, { hide, cleanup, toggle })
}

export function dispose(dotnet, id) {
    const sidebar = sidebars.get(id)

    sidebar?.cleanup()

    sidebars.delete(id)
}