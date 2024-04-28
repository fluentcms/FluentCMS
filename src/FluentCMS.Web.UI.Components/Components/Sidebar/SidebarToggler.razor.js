export function toggle(dotnet, id, isOpen) {
    const sidebar = document.getElementById(id)
    const backdrop = sidebar.nextElementSibling

    if(!sidebar || !backdrop) return;

    sidebar.classList.toggle('-translate-x-full');
    backdrop.classList.toggle('hidden');
    dotnet.invokeMethodAsync("Update", isOpen);

    sidebar.onclick = () => {
        sidebar.classList.add('-translate-x-full');
        backdrop.classList.add('hidden');
        dotnet.invokeMethodAsync("Update", false);
    }
}