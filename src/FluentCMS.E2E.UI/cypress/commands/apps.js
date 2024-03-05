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

Cypress.Commands.add('checkAppCreate', (title, slug, description) => {
    title ||= crypto.randomUUID()
    slug ||= crypto.randomUUID()
    description ||= crypto.randomUUID()

    cy.navigateToAppCreatePage()

    cy.get('#appCreateTitleInput').type(title, { delay: 0 })
    cy.get('#appCreateSlugInput').type(slug, { delay: 0 })
    cy.get('#appCreateDescriptionInput').type(description, { delay: 0 })

    cy.get('#appCreateSubmitButton').click()

    cy.waitForNavigate()

    cy.navigateToAppListPage()

    cy.get('#appListTable').contains(title).should('exist')
    cy.get('#appListTable').contains(slug).should('exist')
    cy.get('#appListTable').contains(description).should('exist')
})

Cypress.Commands.add('checkAppCreateCancel', () => {
    cy.navigateToAppCreatePage()

    const url = window.location.href

    cy.get('#appCreateTitleInput').type('something', { delay: 0 })

    cy.get('#appCreateCancelButton').click()

    cy.waitForNavigate()

    expect(window.location.href).to.eq(url)
})

Cypress.Commands.add('checkAppDetail', () => {
    const title = crypto.randomUUID()
    const slug = crypto.randomUUID()
    const description = crypto.randomUUID()

    cy.checkAppCreate(title, slug, description)

    cy.contains('#appListTable tr', title).then(($row) => {
        cy.wrap($row).find('[data-test="preview-btn"]').click()

        cy.waitForNavigate()

        cy.contains(title).should('be.visible')
        cy.contains(slug).should('be.visible')
        cy.contains(description).should('be.visible')
    });
})

Cypress.Commands.add('checkAppUpdate', (title, slug, description) => {
    title ||= crypto.randomUUID()
    slug ||= crypto.randomUUID()
    description ||= crypto.randomUUID()

    cy.checkAppCreate(title)

    cy.contains('#appListTable tr', title).then(($row) => {
        title = crypto.randomUUID()

        cy.wrap($row).find('[data-test="edit-btn"]').click()

        cy.waitForNavigate()

        cy.contains('Update App').should('be.visible')

        cy.get('#appUpdateTitleInput').clear().type(title, { delay: 0 })

        cy.get('#appUpdateSubmitButton').click()

        cy.waitForNavigate()

        cy.navigateToAppListPage()

        cy.contains(title).should('exist')
    });



})

Cypress.Commands.add('checkAppUpdateCancel', () => {
    const title = crypto.randomUUID()
    const slug = crypto.randomUUID()
    const description = crypto.randomUUID()

    cy.checkAppCreate(title, slug, description)

    const url = window.location.href

    cy.contains('#appListTable tr', title).then(($row) => {
        cy.wrap($row).find('[data-test="edit-btn"]').click()

        cy.waitForNavigate()

        cy.contains('Update App').should('be.visible')

        cy.get('#appUpdateCancelButton').click()

        cy.waitForNavigate()

        expect(window.location.href).to.eq(url)
    });
})

Cypress.Commands.add('checkAppDelete', () => {
    const title = crypto.randomUUID() 

    cy.checkAppCreate(title)

    cy.contains('#appListTable tr', title).then(($row) => { 
        cy.contains(title).should('exist')

        cy.wrap($row).find('[data-test="delete-btn"]').click()

        cy.waitForNavigate()

        cy.get('.f-confirm').find('.f-button').first().click()

        cy.waitForNavigate()

        cy.contains(title).should('not.exist')
    });
})

Cypress.Commands.add('checkAppDeleteCancel', () => {
    const title = crypto.randomUUID()
    const slug = crypto.randomUUID()
    const description = crypto.randomUUID()

    cy.checkAppCreate(title, slug, description)

    cy.contains('#appListTable tr', title).then(($row) => {
        cy.get('.f-confirm').should('not.exist')

        cy.wrap($row).find('[data-test="delete-btn"]').click()

        cy.waitForNavigate()

        cy.get('.f-confirm').should('exist')

        cy.get('.f-confirm').find('.f-button').last().click()

        cy.waitForNavigate()

        cy.get('.f-confirm').should('not.exist')
    });
})
