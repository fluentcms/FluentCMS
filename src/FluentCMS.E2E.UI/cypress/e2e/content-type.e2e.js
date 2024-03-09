import config from "../../config"

describe('Content Type CRUD', () => {
  const app = config.apps[0]

  const contentType = config.contentTypes[0]

  before(() => {
    cy.doSetup()
  
    cy.navigateToContentTypeListPage()
    cy.contentTypeClean()

    cy.navigateToAppListPage()
    cy.appClean()

    cy.navigateToAppCreatePage()
    cy.appCreate(app)
  })

  it('Should not create Content type', () => {
    cy.navigateToContentTypeCreatePage(app.title)

    cy.contentTypeCreateCancel()
  })

  it('Should create Content type', () => {
    cy.navigateToContentTypeCreatePage(app.title)

    cy.contentTypeCreate(contentType)
  })

  it('Should show Content type', () => {

    cy.navigateToContentTypeListPage(app.title)
    cy.contentTypeDetail(contentType)
  })

  it('Should not update Content type', () => {
    cy.navigateToContentTypeListPage(app.title)

    cy.contentTypeUpdateCancel(contentType.slug)
  })

  it('Should update Content type', () => {
    cy.navigateToContentTypeListPage(app.title)

    cy.contentTypeUpdate(app.title, contentType.slug, contentType)
  })

  it('Should show Content type List', () => {
    cy.navigateToAppCreatePage()
    cy.appCreate(config.apps[1])
    
    cy.navigateToContentTypeListPage(app.title)
    cy.shortWait()
    cy.contentTypeList(app.slug)

    cy.get('#contentTypeAppSelect').select(config.apps[1].title)
    cy.contentTypeList(config.apps[1].slug)

    cy.navigateToAppListPage()
    cy.appDelete(config.apps[1].slug)
  })

  it('Should not delete Content type', () => {
    cy.navigateToContentTypeListPage(app.title)

    cy.contentTypeDeleteCancel(contentType.slug)
  })

  it('Should delete Content type', () => {
    cy.navigateToContentTypeListPage(app.title)

    cy.contentTypeDelete(contentType.slug)
  })
})