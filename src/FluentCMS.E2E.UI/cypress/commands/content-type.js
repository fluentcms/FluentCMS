Cypress.Commands.add('navigateToContentTypeListPage', (appTitle) => {
    cy.visit('/')
    cy.get('#adminSidebarContentTypeLink').click()

    cy.shortWait()
    if (appTitle) {
        cy.get('#contentTypeAppSelect').select(appTitle).shortWait()
    }
})

Cypress.Commands.add('navigateToContentTypeCreatePage', (appTitle) => {
    cy.navigateToContentTypeListPage(appTitle)
    cy.get('#contentTypeCreateButton').should('be.visible')
    cy.get('#contentTypeCreateButton').click()

})

Cypress.Commands.add('createContentTypeField', (value) => {
    cy.contains('Update ContentType').should('be.visible')
    cy.contains('Add Field').click()
    cy.get('#contentTypeFieldModalTitle').should('contain', 'Add Field')

    cy.get('#contentTypeFieldModalTitleInput').type(value.title, { delay: 50 })
    cy.get('#contentTypeFieldModalSlugInput').type(value.slug, { delay: 50 })
    cy.get('#contentTypeFieldModalDescriptionInput').type(value.description, { delay: 50 })

    cy.get('#contentTypeFieldModalAdvancedTab').click()

    if (value.label) {
        cy.get('#contentTypeFieldModalLabelInput').type(value.label, { delay: 50 })
    }

    if (value.required) {
        cy.get('#contentTypeFieldModalRequiredInput').click({ force: true })
    }
    if (value.placeholder) {
        cy.get('#contentTypeFieldModalPlaceholderInput').type(value.placeholder, { delay: 50 })
    }
    if (value.hint) {
        cy.get('#contentTypeFieldModalHintInput').type(value.hint, { delay: 50 })
    }
    if (value.defaultValue) {
        cy.get('#contentTypeFieldModalDefaultValueInput').type(value.defaultValue, { delay: 50 })
    }

    cy.get('#contentTypeFieldModalSubmitButton').click()

    cy.shortWait()
    cy.get('#contentTypeFieldModalTitle').should('not.be.visible')
})

Cypress.Commands.add('createContentType', (appTitle, title, slug, description, fields = []) => {
    cy.navigateToContentTypeCreatePage(appTitle)
    cy.get('#contentTypeCreateTitleInput').type(title, { delay: 50 })
    cy.get('#contentTypeCreateSlugInput').type(slug, { delay: 50 })
    cy.get('#contentTypeCreateDescriptionInput').type(description, { delay: 50 })

    cy.get('#contentTypeCreateSubmitButton').click()

    cy.shortWait()
    for (let field of fields) {
        cy.createContentTypeField(field)
    }

    cy.get('#contentTypeUpdateSubmitButton').click()
    //
})

Cypress.Commands.add('cleanContentType', () => {
    cy.navigateToAppListPage()
    cy.cleanApp()
})

Cypress.Commands.add('checkContentTypeDetail', () => {
    cy.get('#contentTypeListTable').rows('posts').each(($row) => {
        cy.wrap($row).get('[data-test="preview-btn"]').click()

        cy.contains('ContentType Detail').should('be.visible')
        cy.contains('Posts').should('be.visible')
        cy.contains('posts').should('be.visible')
        cy.contains('Posts content type').should('be.visible')
    })
})

Cypress.Commands.add('createSampleContentTypes', () => {
    cy.createContentType('First', 'Posts', 'posts', 'Posts content type', [
        { title: 'Title', slug: 'title', description: 'Description of title field', required: true, hint: 'title field', label: 'Title', placeholder: 'Enter Title...', defaultValue: '' },
        { title: 'Content', slug: 'content', description: 'Description of Content field', required: true, hint: 'Content field', label: 'Content', placeholder: 'Enter Content...', defaultValue: '' },
    ])
    cy.createContentType('Second', 'Users', 'users', 'Users content type', [
        { title: 'Name', slug: 'name', description: 'Description of name field', required: true, hint: 'name field', label: 'Name', placeholder: 'Enter Name...', defaultValue: '' },
        { title: 'LastName', slug: 'last-name', description: 'Description of LastName field', required: false, hint: 'Last Name field', label: 'LastName', placeholder: 'Enter Last Name...', defaultValue: '' },
    ])
    cy.createContentType('Second', 'Books', 'books', 'Books content type', [
        { title: 'Title', slug: 'title', description: 'Description of title field', required: true, hint: 'title field', label: 'Title', placeholder: 'Enter Title...', defaultValue: '' }
    ])
})

Cypress.Commands.add('checkContentTypeList', () => {
    // check app switch
})

Cypress.Commands.add('checkContentTypeCreateCancel', () => {

})

Cypress.Commands.add('checkContentTypeCreate', () => {
    cy.createContentType('First', 'Posts', 'posts', 'Posts content type', [
        { title: 'Title', slug: 'title', description: 'Description of title field', required: true, hint: 'title field', label: 'Title', placeholder: 'Enter Title...', defaultValue: '' },
        { title: 'Content', slug: 'content', description: 'Description of Content field', required: true, hint: 'Content field', label: 'Content', placeholder: 'Enter Content...', defaultValue: '' },
    ])
})

Cypress.Commands.add('checkContentTypeUpdateCancel', () => {

})

Cypress.Commands.add('checkContentTypeUpdate', () => {
    cy.navigateToContentTypeListPage('First')

    cy.get('#contentTypeListTable').rows('posts').each(($el) => {
        cy.wrap($el).get('[data-test="edit-btn"]').click()

        cy.get('#contentTypeUpdateTitleInput').clear().type('Posts2', { delay: 50 })
        cy.get('#contentTypeUpdateDescriptionInput').clear().type('Updated Description', { delay: 50 })
        cy.get('#contentTypeUpdateSubmitButton').click()

        cy.navigateToContentTypeListPage('First')

        cy.contains('Posts2').should('be.visible')
    })
})

Cypress.Commands.add('checkContentTypeDeleteCancel', () => {

})

Cypress.Commands.add('checkContentTypeDelete', () => {
    cy.navigateToContentTypeListPage('First')

    cy.get('#contentTypeListTable').rows('posts').each(($el) => {
        cy.wrap($el).deleteRow();

        cy.contains('Posts2').should('not.exist')
    })
})
