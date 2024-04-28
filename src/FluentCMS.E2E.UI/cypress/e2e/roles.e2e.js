import config from "../../config"

describe('Roles CRUD', () => {
  const role = config.roles[0]
  
  beforeEach(() => {
    cy.doSetup()
  })

  before(() => {
    cy.doSetup()
    cy.navigateToRoleListPage()
    cy.roleClean()
  })

  it('Should not create role', () => {
    cy.navigateToRoleCreatePage()

    cy.roleCreateCancel()
  })

  it('Should create role', () => {
    cy.navigateToRoleCreatePage()

    cy.roleCreate(role)
  })

  it('Should show Role', () => {

    cy.navigateToRoleListPage()
    cy.roleDetail(role)
  })

  it('Should not update role', () => {
    cy.navigateToRoleListPage()

    cy.roleUpdateCancel(role.name, role)
  })

  it('Should update role', () => {
    cy.navigateToRoleListPage()

    cy.roleUpdate(role.name, config.roles[1])
  })

  it('Should not delete Role', () => {
    cy.navigateToRoleListPage()

    cy.roleDeleteCancel(config.roles[1].name)
  })

  it('Should delete Role', () => {
    cy.navigateToRoleListPage()

    cy.roleDelete(config.roles[1].name)
  })
})