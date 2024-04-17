
Cypress.Commands.add('getSidebarItem', (id) => {
    cy.waitForNavigate()
    cy.waitForNavigate()
    cy.get('#adminSidebar').then($sidebar => {
        cy.wrap($sidebar).click({ force: true })
    })

    const isMobile = Cypress.config('viewportWidth') < 400

    if (isMobile) {
        cy.get('#adminNavbarToggleButton').click()
        cy.waitForNavigate()
        return cy.get(id)
    } else {
        return cy.get(id)
    }
})

Cypress.Commands.add("checkAdminSidebarNavigations", () => {
    cy.getSidebarItem('#adminSidebarSettingsLink').click()
    cy.get('.f-page-header-title').should('have.text', 'Settings')

    cy.getSidebarItem('#adminSidebarContentTypeLink').click()
    cy.get('.f-page-header-title').should('have.text', 'Content Types List')

    cy.getSidebarItem('#adminSidebarDocsLink a').should('have.attr', 'target', '_blank')
    cy.getSidebarItem('#adminSidebarDocsLink a').invoke('removeAttr', 'target').scrollIntoView().click()
    cy.url().should('include', '/doc/index.html')

    cy.go('back')
})

Cypress.Commands.add('checkAdminSidebarThemeToggle', () => {
    cy.shortWait()
    cy.get('html').should('have.class', 'dark')

    cy.getSidebarItem('#adminThemeButton').click().shortWait()
    cy.get('html').should('not.have.class', 'dark')

    cy.getSidebarItem('#adminThemeButton').click().shortWait()
    cy.get('html').should('have.class', 'dark')
})

Cypress.Commands.add('checkAdminHomeLinks', () => {
    // TODO: Enable this
    // cy.getSidebarItem('#adminSidebarLogoLink').then($el => {
    //     if ($el.length && $el.is(':visible')) {
    //         cy.wrap($el).click()
    //         cy.url().should('eq', Cypress.config().baseUrl + '/')

    //     }
    // })

    cy.getSidebarItem('#adminSidebarHomeLink').click()
    cy.url().should('eq', Cypress.config().baseUrl + '/')
})

Cypress.Commands.add('adminSidebarLogoShouldAvailable', () => {
    // TODO Enable for desktop view
    // cy.getSidebarItem('#adminSidebarLogoLink').should('be.visible')
})

Cypress.Commands.add('adminSidebarShouldAvailable', () => {

    cy.getSidebarItem('#adminSidebar').should('be.visible')

    cy.elementShouldAvailable('#adminSidebarHomeLink a')

    cy.elementShouldAvailable('#adminSidebarContentManagementLink a')

    cy.elementShouldAvailable('#adminSidebarContentTypeLink a')
    cy.elementShouldAvailable('#adminSidebarSettingsLink a')

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

    cy.viewport(1366, 800)
})
