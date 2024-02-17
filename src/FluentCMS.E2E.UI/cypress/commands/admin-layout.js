Cypress.Commands.add("checkAdminSidebarNavigations", () => {

    cy.get('#adminSidebarAppsLink').click()
    cy.get('.f-page-header-title').should('have.text', 'Apps List')

    cy.get('#adminSidebarUsersLink').click()
    cy.get('.f-page-header-title').should('have.text', 'Users List')

    cy.get('#adminSidebarContentTypeLink').click()
    cy.get('.f-page-header-title').should('have.text', 'Content Types List')

    cy.get('#adminSidebarMediaLibraryLink').click()
    cy.get('.f-page-header-title').should('have.text', 'Media Library')

    cy.get('#adminSidebarLoginLink').click()
    cy.contains('Welcome back').should('be.visible')

    cy.go('back').shortWait()
    cy.get('#adminSidebarRegisterLink').click()
    cy.contains('Your Best Work Starts Here').should('be.visible')

    cy.go('back').shortWait()
    cy.get('#adminSidebarForgotLink').click()
    cy.contains('Reset your Password').should('be.visible')

    cy.go('back').shortWait()
    cy.get('#adminSidebarResetLink').click()
    // TODO: Typo (your)
    cy.contains('Reset you Password').should('be.visible')

    cy.go('back').shortWait()
    cy.get('#adminSidebarDocsLink').should('have.attr', 'target', '_blank')
    cy.get('#adminSidebarDocsLink').invoke('removeAttr', 'target').scrollIntoView().click()
    cy.url().should('include', '/doc/index.html')

    cy.go('back')
})

Cypress.Commands.add('checkAdminSidebarThemeToggle', () => {
    cy.get('body').should('not.have.class', 'dark')

    cy.get('#adminThemeButton').click().shortWait()
    cy.get('body').should('have.class', 'dark')

    cy.get('#adminThemeButton').click().shortWait()
    cy.get('body').should('not.have.class', 'dark')
})

Cypress.Commands.add('checkAdminHomeLinks', () => {
    cy.get('#adminSidebarLogoLink').click()
    cy.url().should('eq', Cypress.config().baseUrl + '/')

    cy.get('#adminSidebarLogoLink').click()
    cy.url().should('eq', Cypress.config().baseUrl + '/')
})

Cypress.Commands.add('adminSidebarLogoShouldAvailable', () => {
    cy.elementShouldAvailable('#adminSidebarLogoLink')
})

Cypress.Commands.add('adminSidebarShouldAvailable', () => {
    cy.elementShouldAvailable('#adminSidebar')

    cy.elementShouldAvailable('#adminSidebarHomeLink')

    cy.elementShouldAvailable('#adminSidebarContentManagementLink')

    cy.elementShouldAvailable('#adminSidebarAppsLink')
    cy.elementShouldAvailable('#adminSidebarContentTypeLink')
    cy.elementShouldAvailable('#adminSidebarMediaLibraryLink')
    cy.elementShouldAvailable('#adminSidebarUsersLink')

    cy.elementShouldAvailable('#adminSidebarLoginLink')
    cy.elementShouldAvailable('#adminSidebarRegisterLink')
    cy.elementShouldAvailable('#adminSidebarResetLink')
    cy.elementShouldAvailable('#adminSidebarForgotLink')

    cy.elementShouldAvailable('#adminSidebarDocsLink')
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
