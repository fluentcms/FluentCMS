/// <reference types="cypress" />

Cypress.Commands.add('navigateToFileListPage', () => {
    cy.visit('/').waitForNavigate()
    cy.getSidebarItem('#adminSidebarFileManagementLink').click()

    cy.waitForNavigate()
})

Cypress.Commands.add('navigateToFileCreatePage', () => {
    cy.navigateToFileListPage()
    cy.get('#fileManagementAddButton').should('be.visible')
    cy.get('#fileManagementAddButton').click()
    cy.waitForNavigate()
})

Cypress.Commands.add('fileUploadCancel', (files) => {

    cy.contains('Click to upload').selectFile(files[0].path)

    for(let file of files.slice(1)) {
        cy.get('#fileManagementUploadModalAddNewButton').click()
        cy.contains('Click to upload').selectFile(file.path)    
    }
    
    cy.get("#fileManagementUploadModalCancelButton").click()
    cy.waitForNavigate()

    // TODO: this is not good solution.
    cy.contains(`Files (3)`).should('be.visible')
})

Cypress.Commands.add('fileUpload', (files) => {

    cy.contains('Click to upload').selectFile(files[0].path)

    for(let file of files.slice(1)) {
        cy.get('#fileManagementUploadModalAddNewButton').click()
        cy.contains('Click to upload').selectFile(file.path)    
    }
    
    cy.get("#fileManagementUploadModalUploadButton").click()
    cy.waitForNavigate()

    // TODO: this is not good solution.
    cy.contains(`Files (${3 + files.length})`).should('be.visible')
})

Cypress.Commands.add('fileDetail', (name) => {
    console.log('fileDetail', name)
    cy.contains(name).should('be.visible')
    cy.contains(name).click()

    cy.contains('Details').should('be.visible')
    
})

Cypress.Commands.add('fileUpdateCancel', (name, newName) => {
    cy.fileDetail(name)

    cy.get("#fileManagementEditModalNameInput").clear().type(newName)

    cy.get("#fileManagementEditModalCancelButton").click()

    cy.shortWait()
    cy.contains('Details').should('not.be.visible')  
    cy.contains(name).should('be.visible')
    cy.contains(newName).should('not.exist')
})

Cypress.Commands.add('fileUpdate', (name, newName) => {
    cy.fileDetail(name)

    cy.get("#fileManagementEditModalNameInput").clear().type(newName, {delay: 10})

    cy.get("#fileManagementEditModalUpdateButton").click()

    cy.shortWait()
    cy.contains('Details').should('not.be.visible')  
    
    // TODO: Enable this line
    // cy.contains(name).should('not.exist')
    cy.contains(newName).should('be.visible')
})

Cypress.Commands.add('fileDeleteCancel', (name) => {
    cy.contains('.f-card', name).deleteRow(false, true)

    cy.contains(name).should('be.visible')
})

Cypress.Commands.add('fileDelete', (name) => {
  
    cy.contains('.f-card', name).deleteRow(true, true)

    // TODO: Enable this line
    // cy.contains(name).should('not.exist')
})