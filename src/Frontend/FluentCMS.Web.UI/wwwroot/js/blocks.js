export async function initializeBlocks() {
    const blocks = await fetch('/_content/FluentCMS.Web.UI/blocks.json').then(res => res.json())
    let textHtmlId;

    document.querySelectorAll('.f-plugin-definition-item').forEach(el => {
        if(el.innerHTML.includes('TextHTML')) {
            textHtmlId = el.dataset.id
        }
    })

    for(let block of blocks) {
        const definitionEl = document.createElement('div')
        definitionEl.classList.add('f-plugin-definition-item')
        definitionEl.dataset.id = textHtmlId
        definitionEl.dataset.blockId = block.Id

        definitionEl.innerHTML = `
            <div class="f-plugin-definition-item-content">
                <span class="f-name">${block.Category} - ${block.Name}</span>
                <span class="f-description">${block.Description}</span>
            </div>
        `
        document.querySelector('.f-plugin-definition-list').appendChild(definitionEl)
    }
}