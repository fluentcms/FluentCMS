Cypress.Commands.add('cleanContent', () => {
    cy.contentTypeClean()
    cy.appClean()
})


Cypress.Commands.add('navigateToContentListPage', (appTitle, contentTypeTitle) => {
    cy.visit('/')

    cy.get('#adminSidebarContentManagementLink').click()
    cy.waitForNavigate()
    if(appTitle) {
        cy.get('#contentAppSelect').select(appTitle)
        cy.waitForNavigate()
    }
    cy.get('.f-sidebar-secondary-true').contains(contentTypeTitle).click().waitForNavigate()

    // TODO: Enable this test
    cy.contains(contentTypeTitle + " List").should('be.visible')
})

Cypress.Commands.add('navigateToContentCreatePage', (appTitle, contentTypeTitle) => {
    cy.navigateToContentListPage(appTitle, contentTypeTitle)
    cy.get('#contentCreateButton').click()

    cy.waitForNavigate()
    cy.contains('Create ' + contentTypeTitle).should('be.visible')
})

Cypress.Commands.add('contentCreateCancel', (appTitle, contentTypeTitle) => {
    cy.navigateToContentCreatePage(appTitle, contentTypeTitle)

    cy.get('#contentCreateCancelButton').click()
    cy.waitForNavigate()

    cy.contains(contentTypeTitle + " List").should('be.visible')
})

Cypress.Commands.add('contentCreate', (appTitle, contentTypeTitle, value) => {
    cy.navigateToContentCreatePage(appTitle, contentTypeTitle)

    for(let field in value) {
        cy.get(`#contentCreate${field}Input`).type(value[field], {delay: 50})
    }

    cy.get('#contentCreateSubmitButton').click()
    cy.waitForNavigate()

    cy.contains(contentTypeTitle + " List").should('be.visible')
    
    for(let field in value) {
        cy.contains(value[field])
    }
})

Cypress.Commands.add('contentList', (appTitle, contentTypeTitle) => {
    // TODO: Check list of different apps and content types
    cy.navigateToContentListPage(appTitle, contentTypeTitle)

    // cy.navigateToContentListPage('Second', 'Books')
    
})

Cypress.Commands.add('contentCreateCancel', () => {
    
})


Cypress.Commands.add('contentDeleteCancel', () => {

})

Cypress.Commands.add('contentDelete', (appTitle, contentTypeTitle, text) => {
    cy.navigateToContentListPage(appTitle, contentTypeTitle)

    cy.contains('#contentListTable tr', text).then($row => {
        cy.wrap($row).deleteRow()

        cy.get('#contentListTable').contains(text).should('not.exist')
    })
})

Cypress.Commands.add('contentDeleteCancel', (appTitle, contentTypeTitle, text) => {
    cy.navigateToContentListPage(appTitle, contentTypeTitle)

    cy.contains('#contentListTable tr', text).then($row => {
        cy.wrap($row).deleteRow(false)
        
        cy.get('#contentListTable').contains(text).should('exist')
    })
})

Cypress.Commands.add('contentUpdate', (appTitle, contentTypeTitle, text, value) => {
    cy.navigateToContentListPage(appTitle, contentTypeTitle)

    cy.contains('#contentListTable tr', text).then($row => {
        cy.wrap($row).get('[data-test="edit-btn"]').click()
        
        for(let key in value) {
            cy.get(`#contentUpdate${key}Input`).clear().type(value[key], {delay: 50})
        }
        cy.get('#contentUpdateSubmitButton').click()
        cy.waitForNavigate()
    
        cy.contains(contentTypeTitle + ' List').should('be.visible')
        
        const updatedText = value[Object.keys(value)[0]] 
        cy.get('#contentListTable').contains(updatedText).should('be.visible')
    })
})

Cypress.Commands.add('contentUpdateCancel', (appTitle, contentTypeTitle, text) => {
    cy.navigateToContentListPage(appTitle, contentTypeTitle)

    cy.contains('#contentListTable tr', text).then($row => {
        cy.wrap($row).get('[data-test="edit-btn"]').click()
        
        
        cy.get('#contentUpdateCancelButton').click()
        cy.waitForNavigate()
        cy.contains(contentTypeTitle + ' List').should('be.visible')
        
        cy.get('#contentListTable').contains(text).should('be.visible')
    })
})