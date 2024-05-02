// Home tests

Cypress.Commands.add("navigateToHomePage", () => {
    cy.visit('/').waitForNavigate();
})

Cypress.Commands.add('homeTourShouldExist', () => {
    cy.get('#homeTourTitle').should('be.visible')
})

Cypress.Commands.add('homeVersionShouldExist', () => {
    cy.get('[data-test="version"]').should('exist')
})

Cypress.Commands.add('homeTourButtonsShouldHaveCorrectLinks', () => {
    cy.get('#homeTourCreateContentTypeButton').click().waitForNavigate();
    cy.get('#contentTypeCreateButton').should('be.visible')
    cy.navigateToHomePage()
    cy.get('#homeTourCreateContentButton').click().waitForNavigate();
    cy.get('#contentCreateButton').should('be.visible')
    cy.navigateToHomePage()
    cy.get('#homeTourTestApiButton').click().waitForNavigate();

    // TODO: Implement test api page when API tokens page completed
    // cy.get('#apiTokenCreateButton').should('be.visible')
})

Cypress.Commands.add('homeTourSkip', () => {
    cy.get("#homeTourSkipButton").click();
})

Cypress.Commands.add('homeLinksShouldWork', () => {
    cy.homeTourSkip()
    cy.get('#homeDocsLink').should('have.attr', 'target', '_blank')
    cy.get('#homeDocsLink').invoke('removeAttr', 'target').scrollIntoView().click().waitForNavigate()
    cy.url().should('include', '/api/doc')

    cy.navigateToHomePage()
    cy.homeTourSkip()
    cy.get('#homeExamplesLink').should('have.attr', 'target', '_blank')
    cy.get('#homeExamplesLink').invoke('removeAttr', 'target').scrollIntoView().click().longWait()
    cy.origin('https://github.com', () => {
        cy.contains('FluentCMS').should('be.visible')
    })

    cy.navigateToHomePage()
    cy.homeTourSkip()
    cy.get('#homeChangelogLink').should('have.attr', 'target', '_blank')
    cy.get('#homeChangelogLink').invoke('removeAttr', 'target').scrollIntoView().click().longWait()
    cy.origin('https://github.com', () => {
        cy.contains('CHANGELOG.md').should('be.visible')
    })

    cy.navigateToHomePage()
    cy.homeTourSkip()
    cy.get('#homeGithubLink').should('have.attr', 'target', '_blank')
    cy.get('#homeGithubLink').invoke('removeAttr', 'target').scrollIntoView().click().longWait()
    cy.origin('https://github.com', () => {
        cy.contains('FluentCMS').should('be.visible')
    })

    cy.navigateToHomePage()
    cy.homeTourSkip()
    cy.get('#homeDiscordLink').should('have.attr', 'target', '_blank')
    cy.get('#homeDiscordLink').invoke('removeAttr', 'target').scrollIntoView().click().longWait()
    // cy.url().should('include', 'discord.com')
    cy.origin('https://discord.com', () => {
        cy.contains('FluentCMS').should('be.visible')
    })
})