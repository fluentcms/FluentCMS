Cypress.Commands.add('navigateToAppCreatePage', () => {
    cy.visit('/').shortWait()

    cy.get('#adminSidebarAppsLink').click()
    cy.get('#appCreateButton').click()

    cy.contains('Create App').should('be.visible')

    cy.elementShouldAvailable('#appCreateTitleInput')
    cy.elementShouldAvailable('#appCreateSlugInput')
    cy.elementShouldAvailable('#appCreateDescriptionInput')
    cy.elementShouldAvailable('#appCreateCancelButton')
    cy.elementShouldAvailable('#appCreateSubmitButton')
})

Cypress.Commands.add('cleanApp', () => {
    cy.navigateToAppListPage()

    cy.get('.f-table-body').then(value => {
        console.log(value)
        if (value.children().length > 0) {
            cy.get('.f-table-body .f-table-row').each($el => {
                cy.wrap($el).contains('Delete').click();
                cy.get('.f-confirm-content').should('be.visible')
                cy.get('.f-confirm-content').contains('Yes, I\'m sure').click()
            })
        }
    })
})

Cypress.Commands.add('navigateToAppListPage', () => {
    cy.visit('/').shortWait()
    cy.get('#adminSidebarAppsLink').click().then(() => {
        cy.contains('Apps List').should('be.visible')
    })
})

Cypress.Commands.add('checkAppDeleteCancel', () => {
    cy.navigateToAppListPage()

    cy.get('.f-table-body .f-table-row').each(($el, index, $list) => {
        cy.wrap($el).find('.f-table-cell').contains('first-app').then($column => {
            if ($column.length > 0) {
                cy.wrap($el).contains('Delete').click()

                cy.get('.f-confirm-content').should('be.visible')

                cy.get('.f-confirm-content').contains('No, cancel').click()

                cy.get('.f-table-body .f-table-row').should('contain', 'first-app')
            }
        });
    });
    // Item should not remove
})

Cypress.Commands.add('checkAppDelete', () => {
    cy.navigateToAppListPage()

    cy.get('.f-table-body .f-table-row').each(($el, index, $list) => {
        cy.wrap($el).find('.f-table-cell').contains('first-app').then($column => {
            if ($column.length > 0) {
                cy.wrap($el).contains('Delete').click()

                cy.get('.f-confirm-content').should('be.visible')

                cy.get('.f-confirm-content').contains('Yes, I\'m sure').click()

                cy.get('.f-table-body').should('not.contain', 'first-app')
            }
        });
    });
})

Cypress.Commands.add('checkAppCreateCancel', () => {
    cy.navigateToAppCreatePage();

    cy.get('#appCreateTitleInput').type('something')

    cy.get('#appCreateCancelButton').click().then(() => {
        // TODO: should redirect
        // cy.url().should('eq', Cypress.config().baseUrl + '/admin/apps')
    })
})

Cypress.Commands.add('checkAppCreate', () => {
    cy.navigateToAppCreatePage()

    // fill form
    cy.get('#appCreateTitleInput').type('First App')
    cy.get('#appCreateSlugInput').type('first-app')
    cy.get('#appCreateDescriptionInput').type('Description of first app')

    cy.get('#appCreateSubmitButton').click().then(() => {
    })
    // TODO: should redirect
    // cy.url().should('eq', Cypress.config().baseUrl + '/admin/apps')

    // TODO: Use id
    // cy.get('.f-table-body').contains('First App')
})

Cypress.Commands.add('checkAppList', () => {
    // check list of apps should work
    cy.navigateToAppListPage()

    cy.get('.f-table-body .f-table-row').contains('First App')
})
