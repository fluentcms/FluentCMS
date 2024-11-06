function initializeDropdown() {
    const toggler = document.getElementById("dropdown-toggler")
    const menu = document.getElementById("dropdown-menu")

    toggler.addEventListener('click', (event) => {
        event.stopPropagation();
        menu.classList.toggle("f-toolbar-dropdown-open");
    });

    document.addEventListener('click', (event) => {
        if (!menu.contains(event.target) && !toggler.contains(event.target)) {
            menu.classList.remove("f-toolbar-dropdown-open");
        }
    });
}

export async function initialize(dotnet) {
    initializeDropdown()
}
