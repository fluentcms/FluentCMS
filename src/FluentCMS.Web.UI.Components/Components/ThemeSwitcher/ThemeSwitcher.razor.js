export function toggle(dotnet, isLight) {
    document.documentElement.classList.remove('dark', 'light');

    const theme = isLight ? 'light' : 'dark';

    document.documentElement.classList.add(theme);

    localStorage.setItem("THEME", theme);

    dotnet.invokeMethodAsync("Update", isLight);
}

export function initialize(dotnet) {
    const theme = localStorage.getItem('THEME');

    toggle(dotnet, theme == 'light');
}
