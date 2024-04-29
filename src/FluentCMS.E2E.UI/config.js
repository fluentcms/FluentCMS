export default {
    setupUsername: 'admin',
    setupEmail: 'admin@example.com',
    setupPassword: 'Passw0rd!',

    registerUsername: 'sam',
    registerEmail: 'sam@example.com',
    registerPassword: 'Passw0rd!',

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
    ],
    roles: [
        {
            name: 'ADMIN',
            description: "Role for admins to manage the system",
            permissions: {
                "Posts": {
                    create: true,
                    update: true,
                    delete: true,
                    read: true,
                    publish: true,
                }
            }
        },
        {
            name: 'EDITOR',
            description: "Role for content editors",
            permissions: {
                "Posts": {
                    create: true,
                    update: true,
                    read: true,
                    delete: false,
                    publish: false,
                }
            }
        },
    ],
    users: [
        {
            password: 'Passw0rd!',
            username: 'foo123',
            email: 'foo@gmail.com'
        },
        {
            password: 'Passw0rd!',
            username: 'bar123',
            email: 'bar@gmail.com'
        },
    ],
}
