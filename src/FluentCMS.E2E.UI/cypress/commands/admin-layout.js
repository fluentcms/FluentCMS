Cypress.Commands.add("checkAdminSidebarNavigations", () => {

    cy.log('Should navigate to Apps List page')
    cy.get('#adminSidebarAppsLink').click()
    cy.get('.f-page-header-title').should('have.text', 'Apps List')

    cy.log('Should navigate to Users List page')
    cy.get('#adminSidebarUsersLink').click()
    cy.get('.f-page-header-title').should('have.text', 'Users List')

    cy.log('Should navigate to Content Types List page')
    cy.get('#adminSidebarContentTypeLink').click()
    cy.get('.f-page-header-title').should('have.text', 'Content Types List')

    cy.log('Should navigate to Media Library page')
    cy.get('#adminSidebarMediaLibraryLink').click()
    cy.get('.f-page-header-title').should('have.text', 'Media Library')

    cy.log('Should navigate to Login Page')
    cy.get('#adminSidebarLoginLink').click()
    cy.contains('Welcome back').should('be.visible')

    cy.log('Should navigate to Register Page')
    cy.go(-1).shortWait()
    cy.get('#adminSidebarRegisterLink').click()
    cy.contains('Your Best Work Starts Here').should('be.visible')

    cy.log('Should navigate to Forgot password Page')
    cy.go(-1).shortWait()
    cy.get('#adminSidebarForgotLink').click()
    cy.contains('Reset you Password').should('be.visible')

    cy.log('Should navigate to Reset password Page')
    cy.go(-1).shortWait()
    cy.get('#adminSidebarResetLink').click()
    cy.contains('Reset you Password').should('be.visible')

    cy.log('Should navigate to Documentation Page')
    cy.go(-1).shortWait()
    cy.get('#adminSidebarDocsLink').should('have.attr', 'target', '_blank')
    cy.get('#adminSidebarDocsLink').invoke('removeAttr', 'target').scrollIntoView().click()
    cy.url().should('include', '/doc/index.html')

    cy.go(-1).shortWait()
})

Cypress.Commands.add('checkAdminSidebarThemeToggle', () => {
    cy.log('Default theme should be Light')
    cy.get('body').should('not.have.class', 'dark')

    cy.log('Theme should change to dark if theme button pressed')
    cy.get('#adminThemeButton').click().shortWait()
    cy.get('body').should('have.class', 'dark')

    cy.log('Theme should change to light if theme button pressed again')
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

    // TODO: Not visible in smaller screens, should scroll
    cy.elementShouldAvailable('#adminSidebarDocsLink')
})

Cypress.Commands.add('checkAdminNavbar', () => {
    cy.viewport(600, 800)
    cy.elementShouldAvailable('#adminNavbar')

    cy.elementShouldAvailable('#adminNavbarLogoLink')

    cy.elementShouldAvailable('#adminNavbarToggleButton')

    cy.get('#adminNavbarToggleButton').click()


    // TODO: after click button
})
