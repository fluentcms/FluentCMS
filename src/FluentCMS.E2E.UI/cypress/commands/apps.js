/// <reference types="cypress" />

Cypress.Commands.add('navigateToAppCreatePage', () => {
    cy.navigateToAppListPage()

    cy.get('#appCreateButton').click()

    cy.waitForNavigate()

    cy.contains('Create App').should('be.visible')
})

Cypress.Commands.add('navigateToAppListPage', () => {
    cy.visit('/')

    cy.waitForNavigate()

    cy.get('#adminSidebarAppsLink').click()

    cy.waitForNavigate()

    cy.contains('Apps List').should('be.visible')
})

Cypress.Commands.add('appClean', () => {
    cy.navigateToAppListPage()

    cy.get('#appListTable').deleteTableRows()

    cy.get('#appListTable').should('be.empty')
})

Cypress.Commands.add('appCreate', ({title, slug, description} ={}) => {
    cy.navigateToAppCreatePage()

    cy.get('#appCreateTitleInput').type(title, { delay: 0 })
    cy.get('#appCreateSlugInput').type(slug, { delay: 0 })
    cy.get('#appCreateDescriptionInput').type(description, { delay: 0 })

    cy.get('#appCreateSubmitButton').click()

    cy.waitForNavigate()

    cy.navigateToAppListPage()

    cy.shot('App Create')
    cy.get('#appListTable').contains(title).should('exist')
    cy.get('#appListTable').contains(slug).should('exist')
    cy.get('#appListTable').contains(description).should('exist')
})

Cypress.Commands.add('appCreateCancel', () => {
    cy.navigateToAppCreatePage()

    const url = window.location.href

    cy.get('#appCreateTitleInput').type('something', { delay: 0 })

    cy.get('#appCreateCancelButton').click()

    cy.waitForNavigate()

    cy.shot('App Create Cancel')
    expect(window.location.href).to.eq(url)
})

Cypress.Commands.add('appDetail', ({title, slug, description}) => {
    cy.navigateToAppListPage()

    cy.contains('#appListTable tr', slug).then(($row) => {
        cy.wrap($row).find('[data-test="preview-btn"]').click()

        cy.waitForNavigate()

        cy.shot('App Detail')

        cy.contains(title).should('be.visible')
        cy.contains(slug).should('be.visible')
        cy.contains(description).should('be.visible')
    });
})

Cypress.Commands.add('appUpdate', (appSlug, {title, slug, description} = {}) => {
    cy.navigateToAppListPage()
    
    cy.contains('#appListTable tr', appSlug).then(($row) => {
        
        cy.wrap($row).find('[data-test="edit-btn"]').click()

        cy.waitForNavigate()

        cy.contains('Update App').should('be.visible')

        cy.get('#appUpdateTitleInput').clear().type(title, { delay: 0 })
        cy.get('#appUpdateDescriptionInput').clear().type(description, { delay: 0 })

        cy.get('#appUpdateSubmitButton').click()

        cy.waitForNavigate()

        cy.navigateToAppListPage()

        cy.shot('App Update')

        cy.contains(title).should('exist')
        cy.contains(description).should('exist')
    });
})

Cypress.Commands.add('appUpdateCancel', (appSlug) => {
   
    cy.navigateToAppListPage()
    const url = window.location.href

    cy.contains('#appListTable tr', appSlug).then(($row) => {
        cy.wrap($row).find('[data-test="edit-btn"]').click()

        cy.waitForNavigate()

        cy.contains('Update App').should('be.visible')

        cy.get('#appUpdateCancelButton').click()

        cy.waitForNavigate()
        cy.shot('App Update Cancel')

        expect(window.location.href).to.eq(url)
    });
})

Cypress.Commands.add('appDelete', (appSlug) => {
    cy.navigateToAppListPage()
    cy.contains('#appListTable tr', appSlug).then(($row) => { 

        cy.wrap($row).deleteRow(true)

        cy.shot('App Delete')

        cy.contains(appSlug).should('not.exist')
    });
})

Cypress.Commands.add('appDeleteCancel', (appSlug) => {

    cy.navigateToAppListPage()

    cy.contains('#appListTable tr', appSlug).then(($row) => {

        cy.wrap($row).deleteRow(false)

        cy.shot('App Delete Cancel')

        cy.contains(appSlug).should('exist')
    });
})
