﻿' Signup controller (register new user)
'
' Part of ASP.NET osa framework  www.osalabs.com/osafw/asp.net
' (c) 2009-2015 Oleg Savchuk www.osalabs.com

Imports Microsoft.VisualBasic

Public Class SignupController
    Inherits FwController
    Protected model As New Users
    Public Shared Shadows route_default_action As String = "index"

    Public Overrides Sub init(fw As FW)
        MyBase.init(fw)
        model.init(fw)
        required_fields = "email pwd"
        base_url = "/Signup"
        'override layout
        fw.G("PAGE_LAYOUT") = fw.G("PAGE_LAYOUT_PUBLIC")

        If Not fw.config("IS_SIGNUP") Then fw.redirect(fw.config("UNLOGGED_DEFAULT_URL"))
    End Sub

    Public Sub IndexAction()
        fw.routeRedirect("ShowForm")
    End Sub

    Public Function ShowFormAction() As Hashtable
        Dim ps As Hashtable = New Hashtable
        Dim item As New Hashtable

        If fw.cur_method = "GET" Then 'read from db
            'set defaults here
            'item("field")='default value'
        Else
            'and merge new values from the form
            Utils.mergeHash(item, reqh("item"))
            'here make additional changes if necessary
        End If

        ps("i") = item
        ps("hide_sidebar") = True
        Return ps
    End Function

    Public Sub SaveAction(Optional ByVal form_id As String = "")
        Dim item As Hashtable = reqh("item")
        Dim id As Integer = Utils.f2int(form_id)

        Try
            Validate(item)
            'load old record if necessary
            'Dim itemdb As Hashtable = model.one(id)

            item = FormUtils.filter(item, Utils.qw("email pwd fname lname"))

            If id > 0 Then
                model.update(id, item)
                fw.FLASH("record_updated", 1)
            Else
                item("access_level") = 0
                item("add_users_id") = 0
                id = model.add(item)
                fw.FLASH("record_added", 1)
            End If

            model.doLogin(id)
            fw.redirect(fw.config("LOGGED_DEFAULT_URL"))

        Catch ex As ApplicationException
            fw.G("err_msg") = ex.Message
            Dim args() As [String] = {id}
            fw.route_redirect("ShowForm", Nothing, args)
        End Try
    End Sub

    Public Function Validate(item As Hashtable) As Boolean
        Dim msg As String = ""
        Dim result As Boolean = True
        result = result And validateRequired(item, Utils.qw(required_fields))
        If Not result Then msg = "Please fill in all required fields"

        If result AndAlso model.isExists(item("email"), 0) Then
            result = False
            fw.FERR("email") = "EXISTS"
        End If
        If result AndAlso Not FormUtils.isEmail(item("email")) Then
            result = False
            fw.FERR("email") = "WRONG"
        End If

        If result AndAlso item("pwd") <> item("pwd2") Then
            result = False
            fw.FERR("pwd2") = "WRONG"
        End If

        If Not result Then Throw New ApplicationException(msg)
        Return True
    End Function

End Class
