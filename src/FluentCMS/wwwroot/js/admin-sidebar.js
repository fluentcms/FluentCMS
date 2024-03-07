document.querySelector('[data-drawer-target="adminSidebar"]').addEventListener('click', () => {
    document.getElementById('adminSidebar').classList.toggle('-translate-x-full');
})

document.getElementById('adminSidebar').addEventListener('click', () => {
    document.getElementById('adminSidebar').classList.add('-translate-x-full');
})
