Cypress.Commands.add('cleanApp', () => {
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

Cypress.Commands.add('checkAppCreateCancel', () => {
    cy.get('#appCreateTitleInput').type('something')
    cy.get('#appCreateCancelButton').click()

    // TODO: redirect
    cy.go('back')
    // cy.url().should('eq', Cypress.config().baseUrl + '/admin/apps')
})

Cypress.Commands.add('checkAppCreate', () => {
    cy.get('#appCreateTitleInput').type('First App')
    cy.get('#appCreateSlugInput').type('first-app')
    cy.get('#appCreateDescriptionInput').type('Description of first app')

    cy.get('#appCreateSubmitButton').click()
    
    // TODO: should redirect
    cy.go('back')
    // cy.url().should('eq', Cypress.config().baseUrl + '/admin/apps')

    // TODO: Use id
    cy.get('.f-table-body').contains('First App')
})

Cypress.Commands.add('checkAppUpdateCancel', () => {
    cy.get('.f-table-body .f-table-row').each(($el, index, $list) => {
        cy.wrap($el).find('.f-table-cell').contains('first-app').then($column => {
            if ($column.length > 0) {
                cy.wrap($el).contains('Edit').click()
                cy.contains('Update App')
                cy.get('#appUpdateTitleInput').clear().type('Updated title')
                cy.get('#appUpdateDescriptionInput').clear().type('Updated Description')
                cy.get('#appUpdateCancelButton').click()

                cy.navigateToAppListPage()
                cy.contains('Updated title').should('not.exist')
            }
        });
    });
})

Cypress.Commands.add('checkAppUpdate', () => {
    cy.get('.f-table-body .f-table-row').each(($el, index, $list) => {
        cy.wrap($el).find('.f-table-cell').contains('first-app').then($column => {
            if ($column.length > 0) {
                cy.wrap($el).contains('Edit').click()
                cy.contains('Update App')
                cy.get('#appUpdateTitleInput').clear().type('Updated title')
                cy.get('#appUpdateDescriptionInput').clear().type('Updated Description')
                cy.get('#appUpdateSubmitButton').click()

                cy.go(-1).wait(1000)
                cy.contains('Updated title').should('be.visible')
            }
        });
    });
})

Cypress.Commands.add('checkAppList', () => {
    cy.get('.f-table-body .f-table-row').contains('Updated title')
})

Cypress.Commands.add('checkAppDeleteCancel', () => {
    cy.get('.f-table-body .f-table-row').each(($el, index, $list) => {
        cy.wrap($el).find('.f-table-cell').contains('first-app').then($column => {
            if ($column.length > 0) {
                cy.wrap($el).contains('Delete').click()

                cy.get('.f-confirm-content').should('be.visible')

                cy.get('.f-confirm-content').contains('No, cancel').click()

                cy.get('.f-table-body .f-table-row').should('contain', 'first-app')
            }
        });
    })
})

Cypress.Commands.add('checkAppDelete', () => {
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
