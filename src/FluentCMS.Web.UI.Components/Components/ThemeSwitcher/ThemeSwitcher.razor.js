export function toggle(dotnet, isLight) {
debugger
    document.body.classList.remove('dark', 'light');

    const theme = isLight ? 'light' : 'dark';

    document.body.classList.add(theme);

    localStorage.setItem("THEME", theme);

    dotnet.invokeMethodAsync("Update", isLight);
}

export function initialize(dotnet) {
    const theme = localStorage.getItem('THEME');

    toggle(dotnet, theme == 'light');
}
