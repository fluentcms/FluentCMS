Cypress.Commands.add('navigateToUserListPage', () => {
    cy.visit('/').waitForNavigate()
    cy.getSidebarItem('#adminSidebarUsersLink').click()
    cy.waitForNavigate()
})

Cypress.Commands.add('navigateToUserCreatePage', () => {
    cy.navigateToUserListPage()
    cy.get('#userCreateButton').should('be.visible')
    cy.get('#userCreateButton').click()
    cy.waitForNavigate()
})

Cypress.Commands.add('userCreateCancel', () => {
    cy.get('#userCreateUsernameInput').type('something')

    cy.get('#userCreateCancelButton').click()

    // TODO: Should remove below line 
    cy.go(-1)

    cy.shot('User Create Cancel')
})
Cypress.Commands.add('userCreate', ({ password, email, username }) => {
    cy.get('#userCreateUsernameInput').type(username, { delay: 10 })
    cy.get('#userCreateEmailInput').type(email, { delay: 10 })
    cy.get('#userCreatePasswordInput').type(password, { delay: 10 })

    cy.get('#userCreateSubmitButton').click()
    cy.waitForNavigate()

    cy.navigateToUserListPage()

    cy.shot('User Create')
})

Cypress.Commands.add('userClean', () => {
    cy.get('.f-card').then($el => {
        if($el[0].querySelector('#userListTable')) {
            cy.get('#userListTable').deleteTableRows()
        }
    })

    cy.contains('No Users Found!').should('be.visible')
})

Cypress.Commands.add('userDetail', (user) => {
    cy.contains('#userListTable tr', user.username).then(($row) => {
        cy.wrap($row).find('[data-test="preview-btn"]').click()

        cy.get('main').contains('User Detail').should('be.visible')

        cy.get('main').contains(user.username).should('be.visible')
        cy.get('main').contains(user.email).should('be.visible')
    })
})


Cypress.Commands.add('userUpdateCancel', (userName, user) => {
    cy.contains('#userListTable tr', userName).then(($el) => {
        cy.wrap($el[0]).get('[data-test="edit-btn"]').last().click()

        cy.get('#userUpdateEmailInput').clear().type('updated@gmail.com', { delay: 50 })
        cy.get('#userUpdateCancelButton').click()

        // TODO Should remove this line
        cy.navigateToUserListPage()

        cy.shot('User Update Cancel')
        cy.contains(user.username).should('be.visible')
        cy.contains(user.email).should('be.visible')
    })    
})

Cypress.Commands.add('userUpdate', (userName, user) => {
    cy.contains('#userListTable tr', userName).then(($el) => {
        cy.wrap($el[0]).get('[data-test="edit-btn"]').last().click()

        cy.get('#userUpdateEmailInput').clear().type(user.email, { delay: 10 })
        cy.get('#userUpdateSubmitButton').click()

        // TODO Should remove this line
        cy.navigateToUserListPage()

        cy.shot('User Update')
        cy.contains(user.email).should('be.visible')
    })
})
