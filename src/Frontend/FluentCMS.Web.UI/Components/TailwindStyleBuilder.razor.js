import "/_content/FluentCMS.Web.UI/js/tailwind.cdn.js"

tailwind.config = {
    darkMode: 'class',
    theme: {
        extend: {
            colors: {
                primary: {
                    50: 'hsl(var(--f-primary-50, var(--f-primary), 100%, 95%))',
                    100: 'hsl(var(--f-primary-100, var(--f-primary), 80%, 90%))',
                    200: 'hsl(var(--f-primary-200, var(--f-primary), 70%, 80%))',
                    300: 'hsl(var(--f-primary-300, var(--f-primary), 60%, 70%))',
                    400: 'hsl(var(--f-primary-400, var(--f-primary), 60%, 60%))',
                    500: 'hsl(var(--f-primary-500, var(--f-primary), 60%, 50%))',
                    600: 'hsl(var(--f-primary-600, var(--f-primary), 80%, 40%))',
                    700: 'hsl(var(--f-primary-700, var(--f-primary), 80%, 30%))',
                    800: 'hsl(var(--f-primary-800, var(--f-primary), 80%, 20%))',
                    900: 'hsl(var(--f-primary-900, var(--f-primary), 90%, 15%))',
                },
                surface: {
                    DEFAULT: 'hsl(var(--f-surface))',
                    muted: 'hsl(var(--f-surface-muted))',
                    accent: 'hsl(var(--f-surface-accent))',
                },
                content: {
                    DEFAULT: 'hsl(var(--f-content))',
                    muted: 'hsl(var(--f-content-muted))',
                    accent: 'hsl(var(--f-content-accent))'
                },
                border: {
                    DEFAULT: 'hsl(var(--f-border))',
                    muted: 'hsl(var(--f-border-muted))',
                    accent: 'hsl(var(--f-border-accent))',
                }
            }
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