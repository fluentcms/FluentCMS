/// <reference types="cypress" />

import config from "../../config"

describe('Files CRUD', () => {
  const files = config.files
  
  beforeEach(() => {
    cy.doSetup()
  })

  before(() => {
    cy.doSetup()
    cy.navigateToFileListPage()

    // cy.fileClean()
  })

  it('Should not upload Files', () => {
    cy.navigateToFileCreatePage()
    cy.fileUploadCancel(files)
  })

  it('Should upload Files', () => {
    cy.navigateToFileCreatePage()
    cy.fileUpload(files)
  })

  it('Should show file details', () => {
    cy.navigateToFileCreatePage()
    cy.fileUpload(files)

    cy.fileDetail(files[0].name)
  })

  it('Should not update file', () => {
    cy.navigateToFileCreatePage()
    cy.fileUpload(files)

    cy.fileUpdateCancel(files[0].name, 'updated_name')
  })

  it('Should update file', () => {
    cy.navigateToFileCreatePage()
    cy.fileUpload(files)

    cy.fileUpdate(files[0].name, 'updated_name')
  })


  it('Should not delete file', () => {
    cy.navigateToFileCreatePage()
    cy.fileUpload(files)

    cy.fileDeleteCancel(files[0].name)
  })

  it('Should delete file', () => {
    cy.navigateToFileCreatePage()
    cy.fileUpload(files)

    cy.fileDelete(files[0].name)
  })
})