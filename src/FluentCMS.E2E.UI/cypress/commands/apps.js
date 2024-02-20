const clearTable = (attempt = 0) => {
    if (attempt === 100) throw 'Too many attempts' 
  
    cy.get('.f-table-body').then($tbody => {
      if($tbody.find('.f-table-row').length === 0 ) return;
  
      cy.get('.f-table-row').then($rows => {
        cy.wrap($rows).first().contains('Delete').click();            

        cy.get('.f-confirm-content').should('be.visible')
        cy.get('.f-confirm-content').contains('Yes, I\'m sure').click()
        cy.shortWait()    
  
        cy.then(() => {
          clearTable(++attempt)                        // next step queued using then()
        })
      })
    })
  }
  
  
Cypress.Commands.add('cleanApp', () => {
    cy.navigateToAppListPage()

    clearTable()
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
    cy.get('#appCreateTitleInput').type(title)
    cy.get('#appCreateSlugInput').type(slug)
    cy.get('#appCreateDescriptionInput').type(description)

    cy.get('#appCreateSubmitButton').click()
    
    cy.go('back')
})

Cypress.Commands.add('createSampleApps', () => {
    cy.createApp("First", 'first', 'First App')
    cy.createApp("Second", 'second', 'Second App')
})

Cypress.Commands.add('checkAppCreateCancel', () => {
    cy.get('#appCreateTitleInput').type('something')
    cy.get('#appCreateCancelButton').click()

    // TODO: redirect
    cy.go('back')
    // cy.url().should('eq', Cypress.config().baseUrl + '/admin/apps')
})

Cypress.Commands.add('checkAppCreate', () => {
    cy.createApp('First App', 'first-app', 'Description of first app')

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

Cypress.Commands.add('checkAppDetail', () => {
	cy.get('.f-table-body').find('.f-table-row').then($row => {
		cy.wrap($row).contains('First App').then($column => {
			cy.wrap($row).contains('Preview').click()

			cy.contains('First App').should('be.visible')
			cy.contains('first-app').should('be.visible')
			cy.contains('Description of first app').should('be.visible')
		})
	})
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
