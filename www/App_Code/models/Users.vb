﻿' Users model class
'
' Part of ASP.NET osa framework  www.osalabs.com/osafw/asp.net
' (c) 2009-2013 Oleg Savchuk www.osalabs.com

Public Class Users
    Inherits FwModel
    'ACL constants
    Public Const ACL_VISITOR As Integer = -1
    Public Const ACL_MEMBER As Integer = 0
    Public Const ACL_ADMIN As Integer = 100

    Public Sub New()
        MyBase.New()
        table_name = "users"
        csv_export_fields = "id,fname,lname,email,add_time"
        csv_export_headers = "id,First Name,Last Name,Email,Registered"
    End Sub

    Public Function meId() As Integer
        Return Utils.f2int(fw.SESSION("user_id"))
    End Function

    Public Function oneByEmail(email As String) As Hashtable
        Dim where As Hashtable = New Hashtable
        where("email") = email
        Dim hU As Hashtable = db.row(table_name, where)
        Return hU
    End Function

    Public Function getFullName(id As Object) As String
        Dim result As String = ""
        id = Utils.f2int(id)

        If id > 0 Then
            Dim hU As Hashtable = one(id)
            result = hU("fname") & "  " & hU("lname")
        End If

        Return result
    End Function

    'check if user exists for a given email
    Public Overrides Function isExists(uniq_key As Object, not_id As Integer) As Boolean
        Dim val As String = db.value("select 1 from users where email=" & db.q(uniq_key) & " and id <>" & db.qi(not_id))
        If val = "1" Then
            Return True
        Else
            Return False
        End If
    End Function

    'fill the session and do all necessary things just user authenticated (and before redirect
    Public Function doLogin(id As Integer) As Boolean
        fw.SESSION.Clear()
        fw.SESSION("is_logged", True)
        fw.SESSION("XSS", Utils.getRandStr(16))

        reloadSession(id)

        fw.logEvent("login", id)
        'update login info
        Dim fields As New Hashtable
        fields("login_time") = Now()
        Me.update(id, fields)
        Return True
    End Function

    Public Function reloadSession(Optional id As Integer = 0) As Boolean
        If id = 0 Then id = meId()
        Dim hU As Hashtable = one(id)

        fw.SESSION("user_id", id)
        fw.SESSION("login", hU("email"))
        fw.SESSION("access_level", Utils.f2int(hU("access_level")))
        'fw.SESSION("user", hU)
        Dim fname = Trim(hU("fname"))
        Dim lname = Trim(hU("lname"))
        fw.SESSION("user_name", fname & IIf(fname > "", " ", "") & lname) 'will be empty If no user name Set

        Return True
    End Function

    'return standard list of id,iname where status=0 order by iname
    Public Overrides Function list() As ArrayList
        Dim sql As String = "select id, fname+' '+lname as iname from " & table_name & " where status=0 order by fname, lname"
        Return db.array(sql)
    End Function
    Public Overrides Function listSelectOptions() As ArrayList
        Dim sql As String = "select id, fname+' '+lname as iname from " & table_name & " where status=0 order by fname, lname"
        Return db.array(sql)
    End Function

    ''' <summary>
    ''' check if current user acl is enough. throw exception or return false if user's acl is not enough
    ''' </summary>
    ''' <param name="acl">minimum required access level</param>
    Public Function checkAccess(acl As Integer, Optional is_die As Boolean = True) As Boolean
        Dim users_acl As Integer = Utils.f2int(fw.SESSION("access_level"))

        'check access
        If users_acl < acl Then
            If is_die Then Throw New ApplicationException("Access Denied")
            Return False
        End If

        Return True
    End Function

End Class
