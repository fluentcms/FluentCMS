Cypress.Commands.add('getSidebarItem', (id) => {
    cy.waitForNavigate()
    cy.waitForNavigate()
    cy.get('#adminNavbarToggleButton').then($el => {
        if($el) {
            cy.wrap($el).click()
            cy.waitForNavigate( )
        }
    }).then(() => {
        return cy.get(id)
    })
})

Cypress.Commands.add("checkAdminSidebarNavigations", () => {
    cy.getSidebarItem('#adminSidebarAppsLink').click()
    cy.get('.f-page-header-title').should('have.text', 'Apps List')

    cy.getSidebarItem('#adminSidebarUsersLink').click()
    cy.get('.f-page-header-title').should('have.text', 'Users List')

    cy.getSidebarItem('#adminSidebarContentTypeLink').click()
    cy.get('.f-page-header-title').should('have.text', 'Content Types List')

    cy.getSidebarItem('#adminSidebarDocsLink a').should('have.attr', 'target', '_blank')
    cy.getSidebarItem('#adminSidebarDocsLink a').invoke('removeAttr', 'target').scrollIntoView().click()
    cy.url().should('include', '/doc/index.html')

    cy.go('back')
})

Cypress.Commands.add('checkAdminSidebarThemeToggle', () => {
    cy.get('body').should('not.have.class', 'dark')

    cy.getSidebarItem('#adminThemeButton').click().shortWait()
    cy.get('body').should('have.class', 'dark')

    cy.getSidebarItem('#adminThemeButton').click().shortWait()
    cy.get('body').should('not.have.class', 'dark')
})

Cypress.Commands.add('checkAdminHomeLinks', () => {
    cy.getSidebarItem('#adminSidebarLogoLink').click()
    cy.url().should('eq', Cypress.config().baseUrl + '/')

    cy.getSidebarItem('#adminSidebarLogoLink').click()
    cy.url().should('eq', Cypress.config().baseUrl + '/')
})

Cypress.Commands.add('adminSidebarLogoShouldAvailable', () => {
    cy.getSidebarItem('#adminSidebarLogoLink').should('be.visible')
})

Cypress.Commands.add('adminSidebarShouldAvailable', () => {

    cy.getSidebarItem('#adminSidebar').should('be.visible')

    cy.elementShouldAvailable('#adminSidebarHomeLink a')

    cy.elementShouldAvailable('#adminSidebarContentManagementLink a')

    cy.elementShouldAvailable('#adminSidebarAppsLink a')
    cy.elementShouldAvailable('#adminSidebarContentTypeLink a')
    cy.elementShouldAvailable('#adminSidebarUsersLink a')

    cy.elementShouldAvailable('#adminSidebarDocsLink a')
})

Cypress.Commands.add('checkAdminNavbar', () => {
    cy.viewport(600, 800)
    cy.shortWait()
    cy.elementShouldAvailable('#adminNavbar')

    cy.elementShouldAvailable('#adminNavbarLogoLink')

    cy.elementShouldAvailable('#adminNavbarToggleButton')

    cy.get('#adminNavbarToggleButton').click()
    cy.elementShouldAvailable('#adminSidebar')

    cy.viewport(1366, 768)
})
