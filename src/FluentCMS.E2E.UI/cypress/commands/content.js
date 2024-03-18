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

Cypress.Commands.add('contentCreateCancel', (contentTypeTitle) => {

    cy.get('#contentCreateCancelButton').click()
    cy.waitForNavigate()

    cy.contains(contentTypeTitle + " List").should('be.visible')
    cy.shot('Content Create cancel ' + contentTypeTitle)

})

Cypress.Commands.add('contentCreate', (appTitle, contentTypeTitle, value) => {

    for(let field in value) {
        cy.get(`#contentCreate${field}Input`).type(value[field], {delay: 50})
    }

    cy.get('#contentCreateSubmitButton').click()

    // TODO: Should remove this
    cy.navigateToContentListPage(appTitle, contentTypeTitle)
    cy.waitForNavigate()

    cy.contains(contentTypeTitle + " List").should('be.visible')
    
    for(let field in value) {
        cy.contains(value[field])
    }
    cy.shot('Content Create ' + contentTypeTitle)
    
})

Cypress.Commands.add('contentList', (appTitle, contentTypeTitle) => {
    // TODO: Check list of different apps and content types

    // cy.navigateToContentListPage('Second', 'Books')
    cy.shot('Content List ' + appTitle + ' > ' + contentTypeTitle)
})

Cypress.Commands.add('contentCreateCancel', () => {
    
})


Cypress.Commands.add('contentDeleteCancel', (text) => {
    cy.contains('#contentListTable tr', text).then($row => {
        cy.wrap($row).deleteRow(false)

        cy.waitForNavigate()
        cy.get('#contentListTable').contains(text).should('exist')
        cy.shot('Content Delete Cancel ' + text)
    })
})

Cypress.Commands.add('contentDelete', (text) => {
    cy.contains('#contentListTable tr', text).then($row => {
        cy.wrap($row).deleteRow()

        cy.waitForNavigate()

        cy.get('#contentListTable').contains(text).should('not.exist')
        cy.shot('Content Delete ' + text)
    })
})


Cypress.Commands.add('contentUpdate', (appTitle, contentTypeTitle, text, value) => {
    cy.contains('#contentListTable tr', text).then($row => {
        cy.wrap($row).get('[data-test="edit-btn"]').click()
        
        for(let key in value) {
            cy.get(`#contentUpdate${key}Input`).clear().type(value[key], {delay: 50})
        }
        cy.get('#contentUpdateSubmitButton').click()

        // TODO: Should remove this
        cy.navigateToContentListPage(appTitle, contentTypeTitle)

        cy.waitForNavigate()
    
        cy.contains(contentTypeTitle + ' List').should('be.visible')
        
        const updatedText = value[Object.keys(value)[0]] 
        cy.get('#contentListTable').contains(updatedText).should('be.visible')
        cy.shot('Content Update ' + contentTypeTitle)
    })
})

Cypress.Commands.add('contentUpdateCancel', (contentTypeTitle, text) => {
    cy.contains('#contentListTable tr', text).then($row => {
        cy.wrap($row).get('[data-test="edit-btn"]').click()
        
        
        // TODO: Fix JS interop error and back navigation
        // cy.get('#contentUpdateCancelButton').click()
        // cy.waitForNavigate()
        // cy.contains(contentTypeTitle + ' List').should('be.visible')
        
        // cy.get('#contentListTable').contains(text).should('be.visible')
        // cy.shot('Content Update Cancel ' + contentTypeTitle)
    })
})