import config from "../../config"

describe('Users CRUD', () => {
  const user = config.users[0]
  
  beforeEach(() => {
    cy.doSetup()
  })

  before(() => {
    cy.doSetup()
    cy.navigateToUserListPage()

  })

  it('Should not create user', () => {
    cy.navigateToUserCreatePage()

    cy.userCreateCancel()
  })

  it('Should create user', () => {
    cy.navigateToUserCreatePage()

    cy.userCreate(user)
  })

  it('Should show User', () => {

    cy.navigateToUserListPage()
    cy.userDetail(user)
  })

  it('Should not update user', () => {
    cy.navigateToUserListPage()

    cy.userUpdateCancel(user.username, user)
  })

  it('Should update user', () => {
    cy.navigateToUserListPage()

    cy.userUpdate(user.username, config.users[1])
  })

  it('Should login as the new user', () => {
    cy.visit('/auth/login')
    cy.shortWait()
    cy.checkLogin(user.username, user.password)
  })
})