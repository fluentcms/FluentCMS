import { Columns } from './columns.js';

window.sections = {}

// const actions = {
//     'add-section'() {
//         console.log('add section')
//     }
// }

// document.querySelectorAll('[data-action]').forEach(el => {
//     el.addEventListener('click', () => {
//         actions[el.dataset.action](el)
//     })
// })
window.initializeColumns = ({onResize} = {}) => {
    document.querySelectorAll('.f-row').forEach(row => {
        const column = new Columns(row, {
            gridLines: true,
            onResize,
            colClass: 'f-column',
            breakpointLg: 992,
            breakpointMd: 480,
        })
    
        column.init()
        sections[row.dataset.id] = column
    }) 
}
