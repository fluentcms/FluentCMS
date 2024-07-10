window.sections = {}

document.querySelectorAll('.f-section').forEach(section => {
    const column = new Columns(section, {
        gridLines: true,
        colClass: 'f-plugin-container',
        breakpointLg: 1200,
        breakpointMd: 768,
    })

    column.init()
    sections[section.dataset.name] = column
})
