Cypress.Commands.add("navigateToProfileDetailPage", () => {
    cy.getSidebarItem("#adminSidebarUserInfoButton").click()
    cy.waitForNavigate()
})

Cypress.Commands.add("navigateToProfileUpdatePage", () => {
    cy.navigateToProfileDetailPage()
    cy.get('#profileDetailEditButton').click()
    cy.waitForNavigate()
})


Cypress.Commands.add('profileUpdateCancel', (profile, newProfile) => {
    cy.get('#profileUpdateEmailInput').clear().type(newProfile.email)
    cy.get('#profileUpdateCancelButton').click();
    cy.shortWait()

    cy.navigateToProfileDetailPage();
    cy.shortWait()

    cy.contains(profile.email).should('exist');
    
})
Cypress.Commands.add('profileUpdate', (profile, newProfile) => {
    cy.get('#profileUpdateEmailInput').clear().type(newProfile.email)
    cy.get('#profileUpdateSubmitButton').click();

    cy.navigateToProfileDetailPage();

    cy.contains(newProfile.email).should('exist');  
})


Cypress.Commands.add('profileChangePassword', (oldPassword, newPassword) => {
    cy.get("#profileChangePasswordOldInput").type(oldPassword, {delay: 10})
    cy.get("#profileChangePasswordNewInput").type(newPassword, {delay: 10})

    cy.get("#profileChangePasswordSubmitButton").click().shortWait();
    cy.navigateToProfileDetailPage();
})