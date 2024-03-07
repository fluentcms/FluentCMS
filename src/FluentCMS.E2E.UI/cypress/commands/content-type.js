Cypress.Commands.add('navigateToContentTypeListPage', (appTitle) => {
    cy.visit('/')
    cy.get('#adminSidebarContentTypeLink').click()

    cy.waitForNavigate()
    if (appTitle) {
        cy.get('#contentTypeAppSelect').select(appTitle)
        cy.waitForNavigate()
    }
})

Cypress.Commands.add('navigateToContentTypeCreatePage', (appTitle) => {
    cy.navigateToContentTypeListPage(appTitle)
    cy.get('#contentTypeCreateButton').should('be.visible')
    cy.get('#contentTypeCreateButton').click()
    cy.waitForNavigate()
})

Cypress.Commands.add('contentTypeFieldCreate', (field) => {
    cy.contains('Add Field').click()
    cy.get('#contentTypeFieldModalTitle').should('contain', 'Add Field')

    cy.get('#contentTypeFieldModalTitleInput').type(field.title, { delay: 50 })
    cy.get('#contentTypeFieldModalSlugInput').type(field.slug, { delay: 50 })
    cy.get('#contentTypeFieldModalDescriptionInput').type(field.description, { delay: 50 })

    cy.get('#contentTypeFieldModalAdvancedTab').click()

    if (field.label) {
        cy.get('#contentTypeFieldModalLabelInput').type(field.label, { delay: 50 })
    }

    if (field.required) {
        cy.get('#contentTypeFieldModalRequiredInput').click({ force: true })
    }
    if (field.placeholder) {
        cy.get('#contentTypeFieldModalPlaceholderInput').type(field.placeholder, { delay: 50 })
    }
    if (field.hint) {
        cy.get('#contentTypeFieldModalHintInput').type(field.hint, { delay: 50 })
    }
    if (field.defaultValue) {
        cy.get('#contentTypeFieldModalDefaultValueInput').type(field.defaultValue, { delay: 50 })
    }

    cy.get('#contentTypeFieldModalSubmitButton').click()

    cy.waitForNavigate()
    cy.get('#contentTypeFieldModalTitle').should('not.be.visible')
})

Cypress.Commands.add('contentTypeCreate', (appTitle, { title, slug, description, fields = [] }) => {
    cy.navigateToContentTypeCreatePage(appTitle)
    cy.get('#contentTypeCreateTitleInput').type(title, { delay: 50 })
    cy.get('#contentTypeCreateSlugInput').type(slug, { delay: 50 })
    cy.get('#contentTypeCreateDescriptionInput').type(description, { delay: 50 })

    cy.get('#contentTypeCreateSubmitButton').click()

    cy.waitForNavigate()
    for (let field of fields) {
        cy.contentTypeFieldCreate(field)
    }

    cy.shot('Content Type Create')

    cy.get('#contentTypeUpdateSubmitButton').click()
    cy.waitForNavigate()
})

Cypress.Commands.add('contentTypeClean', (appTitle) => {
    cy.navigateToContentTypeListPage(appTitle)
    cy.get('#contentTypeListTable').deleteTableRows()

    cy.contains('No Content Types Found!').should('be.visible')

    cy.appClean()

})

Cypress.Commands.add('contentTypeDetail', (appTitle, contentType) => {
    cy.navigateToContentTypeListPage(appTitle)

    cy.contains('#contentTypeListTable tr', contentType.slug).then(($row) => {
        cy.wrap($row).find('[data-test="preview-btn"]').click()

        cy.contains('ContentType Detail').should('be.visible')
        cy.contains(contentType.title).should('be.visible')
        cy.contains(contentType.slug).should('be.visible')
        cy.contains(contentType.description).should('be.visible')
        for (let field of contentType.fields) {
            cy.contains(field.title).should('be.visible')
            cy.contains(field.slug).should('be.visible')
        }
    })
})

// Cypress.Commands.add('createSampleContentTypes', () => {
//     cy.createContentType('First', 'Posts', 'posts', 'Posts content type', [
//         { title: 'Title', slug: 'title', description: 'Description of title field', required: true, hint: 'title field', label: 'Title', placeholder: 'Enter Title...', defaultValue: '' },
//         { title: 'Content', slug: 'content', description: 'Description of Content field', required: true, hint: 'Content field', label: 'Content', placeholder: 'Enter Content...', defaultValue: '' },
//     ])
//     cy.createContentType('Second', 'Users', 'users', 'Users content type', [
//         { title: 'Name', slug: 'name', description: 'Description of name field', required: true, hint: 'name field', label: 'Name', placeholder: 'Enter Name...', defaultValue: '' },
//         { title: 'LastName', slug: 'last-name', description: 'Description of LastName field', required: false, hint: 'Last Name field', label: 'LastName', placeholder: 'Enter Last Name...', defaultValue: '' },
//     ])
//     cy.createContentType('Second', 'Books', 'books', 'Books content type', [
//         { title: 'Title', slug: 'title', description: 'Description of title field', required: true, hint: 'title field', label: 'Title', placeholder: 'Enter Title...', defaultValue: '' }
//     ])
// })

Cypress.Commands.add('contentTypeList', () => {
    // check app switch
})

Cypress.Commands.add('contentTypeCreateCancel', () => {
    cy.shot('Content Type Create Cancel')
})

Cypress.Commands.add('contentTypeUpdateCancel', () => {

})

Cypress.Commands.add('contentTypeUpdate', (appTitle, contentTypeSlug, contentType) => {
    cy.navigateToContentTypeListPage(appTitle)

    cy.contains('#contentTypeListTable tr', contentTypeSlug).then(($el) => {
        cy.wrap($el).get('[data-test="edit-btn"]').click()

        cy.get('#contentTypeUpdateTitleInput').clear().type(contentType.title, { delay: 50 })
        cy.get('#contentTypeUpdateDescriptionInput').clear().type(contentType.description, { delay: 50 })
        cy.get('#contentTypeUpdateSubmitButton').click()

        cy.navigateToContentTypeListPage(appTitle)

        cy.contains(contentType.title).should('be.visible')
        cy.contains(contentType.description).should('be.visible')
    })
})

Cypress.Commands.add('contentTypeDeleteCancel', (appTitle, contentTypeSlug) => {
    cy.navigateToContentTypeListPage(appTitle)

    cy.contains('#contentTypeListTable tr', contentTypeSlug).then(($el) => {
        cy.wrap($el).deleteRow(false);

        cy.contains(contentTypeSlug).should('exist')
    })
})

Cypress.Commands.add('contentTypeDelete', (appTitle, contentTypeSlug) => {
    cy.navigateToContentTypeListPage(appTitle)

    cy.contains('#contentTypeListTable tr', contentTypeSlug).then(($el) => {
        cy.wrap($el).deleteRow();

        cy.contains(contentTypeSlug).should('not.exist')
    })
})
