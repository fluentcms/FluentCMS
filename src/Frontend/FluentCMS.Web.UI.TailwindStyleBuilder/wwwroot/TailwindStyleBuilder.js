import "/_content/FluentCMS.Web.UI.TailwindStyleBuilder/tailwind.cdn.js"

export async function initialize(dotnet, config)
{
    tailwind.config = JSON.parse(config)
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