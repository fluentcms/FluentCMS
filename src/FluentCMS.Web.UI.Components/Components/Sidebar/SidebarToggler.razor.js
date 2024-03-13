export function toggle(dotnet, id, isOpen) {
    const sidebar = document.getElementById(id)

    if(!sidebar) return;

    sidebar.classList.toggle('-translate-x-full');
    dotnet.invokeMethodAsync("Update", isOpen);

    sidebar.onclick = () => {
        sidebar.classList.add('-translate-x-full');
        dotnet.invokeMethodAsync("Update", false);

    }
}