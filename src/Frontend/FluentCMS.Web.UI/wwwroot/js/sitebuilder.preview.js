window.sections = {}

document.querySelectorAll('.f-section').forEach(section => {
    const column = new Columns(section, {
        gridLines: true,
        colClass: 'f-plugin-container',
        breakpointLg: 992,
        breakpointMd: 480,
    })

    column.init()
    sections[section.dataset.name] = column
})
