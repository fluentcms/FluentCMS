Cypress.Commands.add('cleanContent', () => {
    cy.cleanApp()
    cy.cleanContentType()

    cy.createSampleApps()
    cy.createSampleContentTypes()

    cy.deleteContents("First", "Posts")
    cy.deleteContents("Second", "Users")
    cy.deleteContents("Second", "Books")
})

Cypress.Commands.add('deleteContents', (appTitle, contentTypeTitle) => {
    cy.navigateToContentListPage(appTitle, contentTypeTitle)
    cy.get('#contentListTable').then($el => {
        if($el[0].querySelector('.f-table-row')) {
            cy.wrap($el).get('.f-table-row').each($row => {
                cy.wrap($el).contains('Delete').click()
                cy.get('.f-confirm-content').should('be.visible')
                cy.contains('Yes, I\'m sure').click()
            })
        }
    })
})
Cypress.Commands.add('navigateToContentListPage', (appTitle, contentTypeTitle) => {
    cy.visit('/')

    cy.get('#adminSidebarContentManagementLink').click()
    cy.shortWait()
    cy.get('#contentAppSelect').select(appTitle)
    cy.shortWait()
    cy.get('.f-sidebar-secondary-true').contains(contentTypeTitle).click().shortWait()
    // TODO: Enable this test
    // cy.contains(contentTypeTitle + " List").should('be.visible')
})

Cypress.Commands.add('navigateToContentCreatePage', (appTitle, contentTypeTitle) => {
    cy.navigateToContentListPage(appTitle, contentTypeTitle)
    cy.get('#contentCreateButton').click()

    cy.shortWait()
    cy.contains('Create ' + contentTypeTitle).should('be.visible')
})

Cypress.Commands.add('createContent', (appTitle, contentTypeTitle, value) => {
    cy.navigateToContentCreatePage(appTitle, contentTypeTitle)

    for(let field in value) {
        cy.get(`#contentCreate${field}Input`).type(value[field])
    }

    cy.get('#contentCreateSubmitButton').click()
    cy.shortWait()

    cy.contains(contentTypeTitle + " List").should('be.visible')
    
    for(let field in value) {
        cy.contains(value[field])
    }
})
Cypress.Commands.add('checkContentList', () => {
    // Check list of different apps and content types
    cy.navigateToContentListPage('First', 'Posts')

    cy.navigateToContentListPage('Second', 'Books')
    
})

Cypress.Commands.add('checkContentCreateCancel', () => {
    
})

Cypress.Commands.add('checkContentCreate', () => {
    cy.createContent('First', 'Posts', {
        title: 'First post',
        content: 'First Post content'
    })

    cy.navigateToContentListPage('First', 'Posts')
    cy.contains('First post').should('be.visible')
})

Cypress.Commands.add('checkContentDeleteCancel', () => {

})

Cypress.Commands.add('checkContentDelete', () => {
    cy.navigateToContentListPage('First', 'Posts')

    cy.get('#contentListTable').then($el => {
        cy.wrap($el).find('.f-table-row').contains('First post updated').then($row => {
            cy.wrap($el).contains('Delete').click()
            cy.get('.f-confirm-content').should('be.visible')
            cy.contains('Yes, I\'m sure').click()

            cy.get('#contentListTable').contains('First post updated').should('not.exist')
        })
    })
})

Cypress.Commands.add('checkContentUpdateCancel', () => {

})

Cypress.Commands.add('checkContentUpdate', () => {
    cy.navigateToContentListPage('First', 'Posts')

    cy.get('#contentListTable').then($el => {
        cy.wrap($el).find('.f-table-row').contains('First post').then($row => {
            cy.wrap($el).contains('Edit').click()
            cy.get('#contentUpdatetitleInput').clear().type('First post updated')
            cy.get('#contentUpdatecontentInput').clear().type('First post content updated')
            cy.get('#contentUpdateSubmitButton').click()
            
            cy.contains('Posts List').should('be.visible')
            
            cy.get('#contentListTable').contains('First post updated').should('be.visible')
        })
    })
})