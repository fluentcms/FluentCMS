export default {
    setupUsername: 'admin',
    setupEmail: 'admin@example.com',
    setupPassword: 'Passw0rd!',

    registerUsername: 'sam',
    registerEmail: 'sam@example.com',
    registerPassword: 'Passw0rd!',

    // apps: [
    //     {
    //         title: 'First App',
    //         slug: 'first-app',
    //         description: 'First App Description'
    //     },
    //     {
    //         title: 'New title',
    //         slug: 'new-slug',
    //         description: 'new-description'
    //     }
    // ],
    contentTypes: [
        {
            title: 'Posts',
            slug: 'posts',
            description: 'description of posts content type',
            fields: [
                {
                    type: 'Text',
                    title: 'Title',
                    slug: 'title',
                    description: 'Description of title field',
                    required: true,
                    hint: 'title field',
                    label: 'Title',
                    placeholder: 'Enter Title...',
                    defaultValue: ''
                },
                {
                    type: 'Text',
                    title: 'Content',
                    slug: 'content',
                    description: 'Description of Content field',
                    required: true,
                    hint: 'Content field',
                    label: 'Content',
                    placeholder: 'Enter Content...',
                    defaultValue: ''
                },
            ]
        }
    ],
    contents: [
        {
            title: 'Title of post',
            content: 'content of post'
        },
        {
            title: 'updated title',
            content: 'updated content'
        }
    ]
}
