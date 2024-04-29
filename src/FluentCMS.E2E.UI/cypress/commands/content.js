const isMobile = Cypress.config('viewportWidth') < 400

Cypress.Commands.add('navigateToContentListPage', (contentTypeTitle) => {
    cy.visit('/')

    cy.getSidebarItem('#adminSidebarContentManagementLink').click()
    cy.waitForNavigate()

    if (isMobile) {
        cy.get('#contentIndexCollectionList').contains(contentTypeTitle).click().waitForNavigate()

    } else {
        cy.get('main').contains(contentTypeTitle).click().waitForNavigate()
    }
    // TODO: Enable this test
    cy.contains(contentTypeTitle + " List").should('be.visible')
})

Cypress.Commands.add('navigateToContentCreatePage', (contentTypeTitle) => {
    cy.navigateToContentListPage(contentTypeTitle)

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

Cypress.Commands.add('contentCreate', (contentTypeTitle, value) => {

    for (let field in value) {
        cy.get(`#contentCreate${field}Input`).type(value[field], { delay: 50 })
    }

    cy.get('#contentCreateSubmitButton').click()

    // TODO: Should remove this
    cy.navigateToContentListPage(contentTypeTitle)
    cy.waitForNavigate()

    cy.contains(contentTypeTitle + " List").should('be.visible')

    for (let field in value) {
        cy.contains(value[field])
    }
    cy.shot('Content Create ' + contentTypeTitle)

})

Cypress.Commands.add('contentList', (contentTypeTitle) => {
    // TODO: Check list of different content types

    // cy.navigateToContentListPage('Second', 'Books')
    cy.shot('Content List ' + contentTypeTitle)
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

        // TODO: Enable this line
        // cy.get('#contentListTable').contains(text).should('not.exist')
        cy.shot('Content Delete ' + text)
    })
})


Cypress.Commands.add('contentUpdate', (contentTypeTitle, text, value) => {
    cy.contains('#contentListTable tr', text).then($row => {
        cy.wrap($row).get('[data-test="edit-btn"]').click()

        for (let key in value) {
            cy.get(`#contentUpdate${key}Input`).clear().type(value[key], { delay: 50 })
        }
        cy.get('#contentUpdateSubmitButton').click()

        // TODO: Should remove this
        cy.navigateToContentListPage(contentTypeTitle)

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