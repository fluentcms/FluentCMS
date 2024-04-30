Cypress.Commands.add('navigateToRoleListPage', () => {
    cy.visit('/').waitForNavigate()
    cy.getSidebarItem('#adminSidebarRolesLink').click()
    cy.waitForNavigate()
})

Cypress.Commands.add('navigateToRoleCreatePage', () => {
    cy.navigateToRoleListPage()
    cy.get('#roleCreateButton').should('be.visible')
    cy.get('#roleCreateButton').click()
    cy.waitForNavigate()
})

Cypress.Commands.add('roleCreate', ({ name, description, permissions = {} }) => {
    cy.get('#roleCreateNameInput').type(name, { delay: 10 })
    cy.get('#roleCreateDescriptionInput').type(description, { delay: 10 })

    for (let permission in permissions) {
        // cy.get('.f-table-body').contains('.f-table-row', permission).then($el => {
        //     if($el.length) {
        //         const el = cy.wrap($el)

        //         // find row with permission title
        //         if(permissions[permission].create) {
        //             el.get('[data-test="roleCreatePermissionCreateInput"]').click({force: true})
        //         }

        //         if(permissions[permission].read) {
        //             el.get('[data-test="roleCreatePermissionReadInput"]').click({force: true})
        //         }

        //         if(permissions[permission].update) {
        //             el.get('[data-test="roleCreatePermissionUpdateInput"]').click({force: true})
        //         }

        //         if(permissions[permission].delete) {
        //             el.get('[data-test="roleCreatePermissionDeleteInput"]').click({force: true})
        //         }

        //         if(permissions[permission].publish) {
        //             el.get('[data-test="roleCreatePermissionPublishInput"]').click({force: true})
        //         }
        //     }

        // })
    }

    cy.get('#roleCreateSubmitButton').click()
    cy.waitForNavigate()

    cy.navigateToRoleListPage()

    cy.shot('Role Create')
})

Cypress.Commands.add('roleClean', () => {
    cy.get('.f-card').then($el => {
        if($el[0].querySelector('#roleListTable')) {
            cy.get('#roleListTable').deleteTableRows()
        }
    })

    cy.contains('No Roles Found!').should('be.visible')
})

Cypress.Commands.add('roleDetail', (role) => {
    cy.contains('#roleListTable tr', role.name).then(($row) => {
        cy.wrap($row).find('[data-test="preview-btn"]').click()

        cy.get('main').contains('Role Detail').should('be.visible')
        cy.get('main').contains(role.name).should('be.visible')
        cy.get('main').contains(role.description).should('be.visible')
        for (let permission in role.permissions) {
            // TODO
            // cy.get('main').contains(field.title).should('be.visible')
            // cy.get('main').contains(field.slug).should('be.visible')
        }
    })
})

Cypress.Commands.add('roleCreateCancel', () => {
    cy.get('#roleCreateNameInput').type('something')
    cy.get('#roleCreateCancelButton').click()

    cy.go(-1)

    cy.shot('Role Create Cancel')
})

Cypress.Commands.add('roleUpdateCancel', (roleName, role) => {
    cy.contains('#roleListTable tr', roleName).then(($el) => {
        cy.wrap($el).get('[data-test="edit-btn"]').click()

        cy.get('#roleUpdateNameInput').clear().type('role name', { delay: 50 })
        cy.get('#roleUpdateDescriptionInput').clear().type('role description', { delay: 50 })
        cy.get('#roleUpdateCancelButton').click()

        // TODO Should remove this line
        cy.navigateToRoleListPage()

        cy.shot('Role Update Cancel')
        cy.get("#roleListTable").contains(role.name).should('be.visible')
        cy.get("#roleListTable").contains(role.description).should('be.visible')
    })    
})

Cypress.Commands.add('roleUpdate', (roleName, role) => {
    cy.contains('#roleListTable tr', roleName).then(($el) => {
        cy.wrap($el).get('[data-test="edit-btn"]').click()

        cy.get('#roleUpdateNameInput').clear().type(role.name, { delay: 10 })
        cy.get('#roleUpdateDescriptionInput').clear().type(role.description, { delay: 10 })
        cy.get('#roleUpdateSubmitButton').click()

        // TODO Should remove this line
        cy.navigateToRoleListPage()

        cy.shot('Role Update')
        cy.get("#roleListTable").contains(role.name).should('be.visible')
        cy.get("#roleListTable").contains(role.description).should('be.visible')
    })
})

Cypress.Commands.add('roleDeleteCancel', (roleName) => {

    cy.contains('#roleListTable tr', roleName).then(($el) => {
        cy.wrap($el).deleteRow(false);

        cy.shot('Role Delete Cancel')
        cy.get("#roleListTable").contains(roleName).should('exist')
    })
})

Cypress.Commands.add('roleDelete', (roleName) => {
    cy.contains('#roleListTable tr', roleName).then(($el) => {
        cy.wrap($el).deleteRow();

        cy.shot('Role Delete')

        // TODO: Conflicts with AdminSidebarUserInfo (both has ADMIN text)
        // cy.contains(roleName).should('not.exist')
    })
})
