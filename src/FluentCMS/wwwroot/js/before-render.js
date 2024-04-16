const theme = localStorage.getItem('THEME') ?? 'light'

document.documentElement.classList.add(theme)
