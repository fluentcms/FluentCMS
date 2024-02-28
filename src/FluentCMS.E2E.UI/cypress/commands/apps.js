/// <reference types="cypress" />

Cypress.Commands.add('deleteApp', (titleOrSlug) => {
    cy.get('#appListTable').rows(titleOrSlug).each($row => {
        cy.wrap($row).deleteRow(confirm);
    })

    cy.get('#appListTable').should('not.contain', titleOrSlug)
})

Cypress.Commands.add('cleanApp', () => {
    cy.navigateToAppListPage()

    cy.get('#appListTable').deleteTableRows()
})

Cypress.Commands.add('navigateToAppCreatePage', () => {
    cy.navigateToAppListPage()
    cy.get('#appCreateButton').click()

    cy.contains('Create App').should('be.visible')

    cy.elementShouldAvailable('#appCreateTitleInput')
    cy.elementShouldAvailable('#appCreateSlugInput')
    cy.elementShouldAvailable('#appCreateDescriptionInput')
    cy.elementShouldAvailable('#appCreateCancelButton')
    cy.elementShouldAvailable('#appCreateSubmitButton')
})


Cypress.Commands.add('navigateToAppListPage', () => {
    cy.visit('/').shortWait()
    cy.get('#adminSidebarAppsLink').click().then(() => {
        cy.contains('Apps List').should('be.visible')
    })
})

Cypress.Commands.add('createApp', (title, slug, description) => {
    cy.navigateToAppCreatePage()
    cy.get('#appCreateTitleInput').type(title, { delay: 50 })
    cy.get('#appCreateSlugInput').type(slug, { delay: 50 })
    cy.get('#appCreateDescriptionInput').type(description, { delay: 50 })

    cy.get('#appCreateSubmitButton').click()

    cy.go('back')
})

Cypress.Commands.add('createSampleApps', () => {
    cy.createApp("First", 'first', 'First App')
    cy.createApp("Second", 'second', 'Second App')
})

Cypress.Commands.add('checkAppCreateCancel', () => {
    cy.get('#appCreateTitleInput').type('something', { delay: 50 })
    cy.get('#appCreateCancelButton').click()

    // TODO: redirect
    cy.go('back')
    // cy.url().should('eq', Cypress.config().baseUrl + '/admin/apps')
})

Cypress.Commands.add('checkAppCreate', () => {
    cy.createApp('First App', 'first-app', 'Description of first app')

    cy.get('#appListTable').contains('First App')
})

Cypress.Commands.add('checkAppUpdateCancel', () => {
    cy.get('#appListTable').rows('first-app').each(($row) => {
        cy.wrap($row).contains('[data-test="edit-btn"]').click()
        cy.contains('Update App')
        cy.get('#appUpdateTitleInput').clear().type('Updated title', { delay: 50 })
        cy.get('#appUpdateDescriptionInput').clear().type('Updated Description', { delay: 50 })
        cy.get('#appUpdateCancelButton').click()

        cy.navigateToAppListPage()
        cy.contains('Updated title').should('not.exist')
    });
})

Cypress.Commands.add('checkAppUpdate', () => {
    cy.get('#appListTable').rows('first-app').each(($row) => {
        cy.wrap($row).get('[data-test="edit-btn"]').click()
        cy.contains('Update App')
        cy.get('#appUpdateTitleInput').clear().type('Updated title', { delay: 50 })
        cy.get('#appUpdateDescriptionInput').clear().type('Updated Description', { delay: 50 })
        cy.get('#appUpdateSubmitButton').click()

        cy.go(-1).shortWait()
        cy.contains('Updated title').should('be.visible')
    });
})

Cypress.Commands.add('checkAppDetail', () => {
    cy.get('#appListTable').rows('First App').each($row => {
        cy.wrap($row).get('[data-test="preview-btn"]').click()

        cy.contains('First App').should('be.visible')
        cy.contains('first-app').should('be.visible')
        cy.contains('Description of first app').should('be.visible')
    })
})

Cypress.Commands.add('checkAppList', () => {
    cy.get('#appListTable').contains('Updated title')
})

Cypress.Commands.add('checkAppDeleteCancel', () => {
    cy.get('#appListTable').rows('first-app').each(($row) => {
        cy.wrap($row).deleteRow(false)

        cy.get('#appListTable .f-table-row').should('contain', 'first-app')
    })
})

Cypress.Commands.add('checkAppDelete', () => {
    cy.deleteApp('first-app')
})