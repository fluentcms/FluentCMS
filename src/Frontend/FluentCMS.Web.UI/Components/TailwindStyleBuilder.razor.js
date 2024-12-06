import "/_content/FluentCMS.Web.UI/js/tailwind.cdn.js"

tailwind.config = {
    darkMode: 'class',
    theme: {
        extend: {
            colors: {
                primary: {
                    50: '#f4f8fd',
                    100: '#e8f1fb',
                    200: '#c6ddf4',
                    300: '#a3c8ed',
                    400: '#5e9fe0',
                    500: '#1976d2',
                    600: '#176abd',
                    700: '#13599e',
                    800: '#0f477e',
                    900: '#0c3a67',
                    on: '#f4f8fd',
                    dark: {
                        DEFAULT: '#a3c8ed',
                        on: '#0f477e',
                    }
                },
            },
        },
    }
}

export async function initialize(dotnet)
{
    await new Promise(resolve => setTimeout(resolve, 1000));

    const styleTags = document.querySelectorAll('style')
    let result = ''

    styleTags.forEach(style => {
        if(style.textContent.slice(0, 20).includes('tailwind')){
            result = style.textContent
        }
    })
    
    return result
}