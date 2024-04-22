import config from "../../config"

describe('Content Type CRUD', () => {
  const contentType = config.contentTypes[0]
  
  beforeEach(() => {
    cy.doSetup()
  })

  before(() => {
    cy.doSetup()
    cy.navigateToContentTypeListPage()
    cy.contentTypeClean()
  })

  it('Should not create Content type', () => {
    cy.navigateToContentTypeCreatePage()

    cy.contentTypeCreateCancel()
  })

  it('Should create Content type', () => {
    cy.navigateToContentTypeCreatePage()

    cy.contentTypeCreate(contentType)
  })

  it('Should show Content type', () => {
    // cy.navigateToContentTypeListPage()
    // cy.contentTypeDetail(contentType)
  })

  it('Should not update Content type', () => {
    cy.navigateToContentTypeListPage()

    cy.contentTypeUpdateCancel(contentType.slug)
  })

  it('Should update Content type', () => {
    cy.navigateToContentTypeListPage()

    cy.contentTypeUpdate(contentType.slug, contentType)
  })

  it('Should show Content type List', () => {   
    cy.navigateToContentTypeListPage()
    cy.shortWait()
    cy.contentTypeList()
  })

  it('Should not delete Content type', () => {
    cy.navigateToContentTypeListPage()

    cy.contentTypeDeleteCancel(contentType.slug)
  })

  it('Should delete Content type', () => {
    cy.navigateToContentTypeListPage()

    cy.contentTypeDelete(contentType.slug)
  })
})