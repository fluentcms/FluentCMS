var toggleBtn = document.querySelector('[data-drawer-target="adminSidebar"]')
var sidebar = document.getElementById('adminSidebar')

if(sidebar) {
    if(toggleBtn) {
        toggleBtn.addEventListener('click', () => {
            sidebar.classList.toggle('-translate-x-full');
        })    
    }

    sidebar.addEventListener('click', () => {
        sidebar.classList.add('-translate-x-full');
    })    
}

