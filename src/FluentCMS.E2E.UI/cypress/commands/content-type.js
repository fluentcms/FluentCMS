Cypress.Commands.add('navigateToContentTypeListPage', (appTitle) => {
    cy.visit('/')
    cy.get('#adminSidebarContentTypeLink').click()

    cy.waitForNavigate()
    cy.get('#contentTypeAppSelect').should('be.visible')

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
    cy.shot('Content type add field ' + field.title)
})

Cypress.Commands.add('contentTypeCreate', ({ title, slug, description, fields = [] }) => {
    cy.get('#contentTypeCreateTitleInput').type(title, { delay: 50 })
    cy.get('#contentTypeCreateSlugInput').type(slug, { delay: 50 })
    cy.get('#contentTypeCreateDescriptionInput').type(description, { delay: 50 })

    cy.get('#contentTypeCreateSubmitButton').click()

    cy.waitForNavigate()
    for (let field of fields) {
        cy.contentTypeFieldCreate(field)
    }

    cy.get('#contentTypeUpdateSubmitButton').click()
    cy.waitForNavigate()

    cy.navigateToContentTypeListPage()

    cy.shot('Content Type Create')
})

Cypress.Commands.add('contentTypeClean', () => {
    cy.get('#contentTypeListTable').deleteTableRows()

    // TODO: Enable this
    // cy.contains('No Content Types Found!').should('be.visible')
})

Cypress.Commands.add('contentTypeDetail', (contentType) => {
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

Cypress.Commands.add('contentTypeList', (appTitle) => {
    // check app switch
    cy.get('#contentTypeAppSelect').should('have.value', appTitle)

    cy.shot('Content Type List ' + appTitle)
})

Cypress.Commands.add('contentTypeCreateCancel', () => {
    cy.shot('Content Type Create Cancel')
})

Cypress.Commands.add('contentTypeUpdateCancel', () => {
    cy.shot('Content Type Update Cancel')

})

Cypress.Commands.add('contentTypeUpdate', (appTitle, contentTypeSlug, contentType) => {
    cy.contains('#contentTypeListTable tr', contentTypeSlug).then(($el) => {
        cy.wrap($el).get('[data-test="edit-btn"]').click()

        cy.get('#contentTypeUpdateTitleInput').clear().type(contentType.title, { delay: 50 })
        cy.get('#contentTypeUpdateDescriptionInput').clear().type(contentType.description, { delay: 50 })
        cy.get('#contentTypeUpdateSubmitButton').click()

        // TODO Should remove this line
        cy.navigateToContentTypeListPage(appTitle)

        cy.shot('Content Type Update')
        cy.contains(contentType.title).should('be.visible')
        cy.contains(contentType.description).should('be.visible')
    })
})

Cypress.Commands.add('contentTypeDeleteCancel', (contentTypeSlug) => {

    cy.contains('#contentTypeListTable tr', contentTypeSlug).then(($el) => {
        cy.wrap($el).deleteRow(false);

        cy.shot('Content Type Delete')
        cy.contains(contentTypeSlug).should('exist')
    })
})

Cypress.Commands.add('contentTypeDelete', (contentTypeSlug) => {
    cy.contains('#contentTypeListTable tr', contentTypeSlug).then(($el) => {
        cy.wrap($el).deleteRow();

        cy.shot('Content Type Delete')
        cy.contains(contentTypeSlug).should('not.exist')
    })
})
