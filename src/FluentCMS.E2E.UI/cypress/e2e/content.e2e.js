import config from "../../config"

describe('Content CRUD', () => {
  const app = config.apps[0]

  const contentType = config.contentTypes[0]

  const content = config.contents[0]

  before(() => {
    cy.doSetup()

    cy.navigateToContentTypeListPage()
    cy.contentTypeClean()


    cy.navigateToAppListPage()
    cy.appClean()

    cy.navigateToAppCreatePage()

    cy.appCreate(app)

    cy.navigateToContentTypeCreatePage(app.title)
    cy.contentTypeCreate(contentType)
  })

  it('Should not create Content', () => {
    cy.navigateToContentListPage(app.title, contentType.title)
    
    cy.contentCreateCancel(contentType.title)
  })

  it('Should create Content', () => {
    cy.navigateToContentCreatePage(app.title, contentType.title)

    cy.contentCreate(app.title, contentType.title, content)
  })

  it('Should not update Content', () => {
    cy.navigateToContentListPage(app.title, contentType.title)

    cy.contentUpdateCancel(contentType.title, content.title)
  })

  it('Should update Content', () => {
    cy.navigateToContentListPage(app.title, contentType.title)

    cy.contentUpdate(app.title, contentType.title, content.title, config.contents[1])
  })

  it('Should show Content List', () => {
    cy.navigateToContentListPage(app.title, contentType.title)

    cy.contentList(app.title, contentType.title)
  })

  it('Should not delete Content', () => {
    cy.navigateToContentListPage(app.title, contentType.title)
    cy.contentDeleteCancel(config.contents[1].title)
  })

  it('Should delete Content', () => {
    cy.navigateToContentListPage(app.title, contentType.title)
    cy.contentDelete(config.contents[1].title)
  })
})