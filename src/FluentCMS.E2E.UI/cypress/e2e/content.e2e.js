import config from "../../config"

describe('Content CRUD', () => {
  const contentType = config.contentTypes[0]

  const content = config.contents[0]

  beforeEach(() => {
    cy.doSetup()
  })

  before(() => {
    cy.doSetup()

    cy.navigateToContentTypeListPage()
    cy.contentTypeClean()

    cy.navigateToContentTypeCreatePage()
    cy.contentTypeCreate(contentType)
  })

  it('Should not create Content', () => {
    cy.navigateToContentListPage(contentType.title)

    cy.contentCreateCancel(contentType.title)
  })

  it('Should create Content', () => {
    cy.navigateToContentCreatePage(contentType.title)

    cy.contentCreate(contentType.title, content)
  })

  it('Should not update Content', () => {
    cy.navigateToContentListPage(contentType.title)

    cy.contentUpdateCancel(contentType.title, content.title)
  })

  it('Should update Content', () => {
    cy.navigateToContentListPage(contentType.title)

    cy.contentUpdate(contentType.title, content.title, config.contents[1])
  })

  it('Should show Content List', () => {
    cy.navigateToContentListPage(contentType.title)

    cy.contentList(contentType.title)
  })

  it('Should not delete Content', () => {
    cy.navigateToContentListPage(contentType.title)
    cy.contentDeleteCancel(config.contents[1].title)
  })

  it('Should delete Content', () => {
    cy.navigateToContentListPage(contentType.title)
    cy.contentDelete(config.contents[1].title)
  })
})