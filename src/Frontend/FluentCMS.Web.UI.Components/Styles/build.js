const { compileAsync } = require("sass");
const {writeFile} = require('fs/promises')
async function build() {
    const res1 = await compileAsync('./Flowbite/SiteBuilder/Default.scss').then(res => res.css)
    await writeFile('../wwwroot/css/default.css', res1)

    const res2 = await compileAsync('./Flowbite/SiteBuilder/DefaultAdmin.scss').then(res => res.css)
    await writeFile('../wwwroot/css/default-admin.css', res2)
    
    const res3 = await compileAsync('./Flowbite/SiteBuilder/Preview.scss').then(res => res.css)
    await writeFile('../wwwroot/css/preview.css', res3)

    const res4 = await compileAsync('./Flowbite/SiteBuilder/Editor.scss').then(res => res.css)
    await writeFile('../wwwroot/css/editor.css', res4)
}

build()