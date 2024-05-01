/// <reference types="cypress" />

import config from "../../config"

describe('Files CRUD', () => {
  const files = config.files
  const multipleFiles = [
    files[0],
    files[0]
  ]
  
  beforeEach(() => {
    cy.doSetup()
  })

  before(() => {
    cy.doSetup()
    cy.navigateToFileListPage()
    // TODO: Should test EmptyState (Issue #1082) by removing all files
    // cy.fileClean() 
  })

  it('Should not upload Files', () => {
    cy.navigateToFileCreatePage()
    cy.fileUploadCancel(multipleFiles)
  })

  it('Should upload Files', () => {
    cy.navigateToFileCreatePage()
    cy.fileUpload(multipleFiles)
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