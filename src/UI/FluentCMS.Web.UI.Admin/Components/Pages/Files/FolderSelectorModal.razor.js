
// <script type="module">
//     import { TreeSelector } from '/js/tree-selector.js'

//     const element = document.getElementById("folderSelectorInput");
//     const folderIcon = '<svg aria-hidden="true" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" class="icon icon-tabler icons-tabler-outline icon-tabler-folder"><path stroke="none" d="M0 0h24v24H0z" fill="none"/><path d="M5 4h4l3 3h7a2 2 0 0 1 2 2v8a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2v-11a2 2 0 0 1 2 -2" /></svg>'

//     let currentFolderId = null

//     let rootFolder = await fetch('/api/Folders/GetRoot?siteId=@(SiteId)').then(res => res.json())

//     async function getFolderItems(id) {

//         let folderResponse = await fetch('/api/Folders/GetById/' + id).then(res => res.json())
//         let options = []

//         for (let folder of folderResponse.folders) {

//             options.push({
//                 key: folder.id,
//                 text: folder.name,
//                 icon: folderIcon,
//                 items: await getFolderItems(folder.id)
//             });
//         }
//         return options
//     }

//     let options = [
//         {
//             key: rootFolder.id,
//             text: 'Root',
//             icon: folderIcon,
//             items: []
//         }]

//     for (let folder of rootFolder.folders) {
//         options[0].items.push({
//             key: folder.id,
//             text: folder.name,
//             icon: folderIcon,
//             items: await getFolderItems(folder.id)
//         })
//     }

//     let instance = new TreeSelector(element, {
//         cssPrefix: 'f-form-tree-selector',
//         options,
//         onChange: (id) => {
//             @* console.log('selected: ', id) *@
//                 @* document.getElementById('folderSelectorValueInput').value = id *@
//         },
//         value: document.getElementById('folderSelectorInput').value
//     })

// </script>
