﻿' Fw Controller base class
'
' Part of ASP.NET osa framework  www.osalabs.com/osafw/asp.net
' (c) 2009-2017 Oleg Savchuk www.osalabs.com

Public MustInherit Class FwController
    Public Shared route_default_action As String = "" 'supported values - "" (use Default Parser for unknown actions), index (use IndexAction for unknown actions), show (assume action is id and use ShowAction)
    Public base_url As String 'base url for the controller

    Public form_new_defaults As Hashtable   'optional, defaults for the fields in new form
    Public required_fields As String        'optional, default required fields, space-separated
    Public save_fields As String            'required, fields to save from the form to db, space-separated
    Public save_fields_checkboxes As String 'optional, checkboxes fields to save from the form to db, qw string: "field|def_value field2|def_value2"

    Protected fw As FW
    Protected db As DB
    Protected model0 As FwModel
    Protected config As Hashtable                  ' controller config, loaded from template dir/config.json

    Protected list_view As String                  ' table/view to use in list sql, if empty model0.table_name used
    Protected list_orderby As String               ' orderby for the list screen
    Protected list_filter As Hashtable             ' filter values for the list screen
    Protected list_where As String = " 1=1 "       ' where to use in list sql, default is non-deleted records (see setListSearch() )
    Protected list_count As Integer                ' count of list rows returned from db
    Protected list_rows As ArrayList               ' list rows returned from db (array of hashes)
    Protected list_pager As ArrayList              ' pager for the list from FormUtils.get_pager
    Protected list_sortdef As String               ' required for Index, default list sorting: name asc|desc
    Protected list_sortmap As Hashtable            ' required for Index, sortmap fields
    Protected search_fields As String              ' optional, search fields, space-separated 
    'fields to search via $s=list_filter("s"), ! - means exact match, not "like"
    'format: "field1 field2,!field3 field4" => field1 LIKE '%$s%' or (field2 LIKE '%$s%' and field3='$s') or field4 LIKE '%$s%'

    'support of customizable view list
    'map of fileld names to screen names
    Protected is_dynamic As Boolean = False         'true if controller is dynamic, then define below:
    Protected view_list_defaults As String = ""     'qw list of default columns
    Protected view_list_map As String = ""          'qh list of all available columns fieldname|visiblename
    Protected view_list_map_cache As Hashtable      'cache for view_list_map as hash
    Protected view_list_custom As String = ""       'qw list of custom-formatted fields for the list_table

    Protected return_url As String                 ' url to return after SaveAction successfully completed, passed via request
    Protected related_id As String                 ' related id, passed via request. Controller should limit view to items related to this id
    Protected related_field_name As String         ' if set (in Controller) and $related_id passed - list will be filtered on this field

    Public Sub New(Optional fw As FW = Nothing)
        If fw IsNot Nothing Then
            Me.fw = fw
            Me.db = fw.db
        End If
    End Sub

    Public Overridable Sub init(fw As FW)
        Me.fw = fw
        Me.db = fw.db

        return_url = reqs("return_url")
        related_id = reqs("related_id")
    End Sub

    'load controller config from json in template dir (based on base_url)
    Public Overridable Sub loadControllerConfig()
        Dim conf_file0 = base_url.ToLower() & "/config.json"
        Dim conf_file = fw.config("template") & "/" & conf_file0
        If Not IO.File.Exists(conf_file) Then Throw New ApplicationException("Controller Config file not found in templates: " & conf_file0)

        Me.config = Utils.jsonDecode(FW.get_file_content(conf_file))
        If Me.config Is Nothing Then Throw New ApplicationException("Controller Config is invalid, check json in templates: " & conf_file0)
        'logger("loaded config:")
        'logger(Me.config)

        'TODO check/conv to str
        required_fields = Utils.f2str(Me.config("required_fields"))
        save_fields = Utils.f2str(Me.config("save_fields"))
        save_fields_checkboxes = Utils.f2str(Me.config("save_fields"))

        search_fields = Utils.f2str(Me.config("search_fields"))
        list_sortdef = Utils.f2str(Me.config("list_sortdef"))
        list_sortmap = Utils.qh(Utils.f2str(Me.config("list_sortmap")))

        related_field_name = Utils.f2str(Me.config("related_field_name"))

        list_view = Utils.f2str(Me.config("list_view"))

        is_dynamic = Utils.f2bool(Me.config("is_dynamic"))
        If is_dynamic Then
            'Whoah! this is fully dynamic form
            view_list_defaults = Utils.f2str(Me.config("view_list_defaults"))
            view_list_map = Utils.f2str(Me.config("view_list_map"))
            view_list_custom = Utils.f2str(Me.config("view_list_custom"))

            list_sortmap = getViewListSortmap() 'just add all fields from view_list_map
            search_fields = getViewListUserFields() 'just search in all visible fields
        End If

    End Sub

    'set of helper functions to return string, integer, date values from request (fw.FORM)
    Public Function req(iname As String) As Object
        Return fw.FORM(iname)
    End Function
    Public Function reqh(iname As String) As Hashtable
        If fw.FORM(iname) IsNot Nothing AndAlso fw.FORM(iname).GetType() Is GetType(Hashtable) Then
            Return fw.FORM(iname)
        Else
            Return New Hashtable
        End If
    End Function

    Public Function reqs(iname As String) As String
        Dim value As String = fw.FORM(iname)
        If IsNothing(value) Then value = ""
        Return value
    End Function
    Public Function reqi(iname As String) As Integer
        Return Utils.f2int(fw.FORM(iname))
    End Function
    Public Function reqd(iname As String) As Object
        Return Utils.f2date(fw.FORM(iname))
    End Function

    Public Sub rw(ByVal str As String)
        fw.rw(str)
    End Sub

    '----------------- just a covenience methods
    Public Sub logger(ByRef dmp_obj As Object)
        fw.logger("DEBUG", dmp_obj)
    End Sub

    'return hashtable of filter values
    'NOTE: automatically set to defaults - pagenum=0 and pagesize=MAX_PAGE_ITEMS
    'NOTE: if request param 'dofilter' passed - session filters cleaned
    'sample in IndexAction: me.get_filter()
    Public Overridable Function initFilter(Optional session_key As String = Nothing) As Hashtable
        Dim f As Hashtable = fw.FORM("f")
        If f Is Nothing Then f = New Hashtable

        If IsNothing(session_key) Then session_key = "_filter_" & fw.G("controller.action")

        Dim sfilter As Hashtable = fw.SESSION(session_key)
        If sfilter Is Nothing OrElse Not (TypeOf sfilter Is Hashtable) Then sfilter = New Hashtable

        'if not forced filter - merge form filters to session filters
        Dim is_dofilter As Boolean = fw.FORM.ContainsKey("dofilter")
        If Not is_dofilter Then
            Utils.mergeHash(sfilter, f)
            f = sfilter
        End If

        'paging
        If Not f.ContainsKey("pagenum") OrElse Not Regex.IsMatch(f("pagenum"), "^\d+$") Then f("pagenum") = 0
        If Not f.ContainsKey("pagesize") OrElse Not Regex.IsMatch(f("pagesize"), "^\d+$") Then f("pagesize") = fw.config("MAX_PAGE_ITEMS")

        'save in session for later use
        fw.SESSION(session_key, f)

        Me.list_filter = f
        Return f
    End Function

    ''' <summary>
    ''' Validate required fields are non-empty and set global fw.ERR[field] values in case of errors
    ''' </summary>
    ''' <param name="item">fields/values to validate</param>
    ''' <param name="fields">field names required to be non-empty (trim used)</param>
    ''' <returns>true if all required field names non-empty</returns>
    ''' <remarks>also set global fw.ERR[REQUIRED]=true in case of validation error</remarks>
    Public Overloads Function validateRequired(item As Hashtable, fields As Array) As Boolean
        Dim result As Boolean = True
        If item IsNot Nothing AndAlso IsArray(fields) AndAlso fields.Length > 0 Then
            For Each fld As String In fields
                If fld > "" AndAlso (Not item.ContainsKey(fld) OrElse Trim(item(fld)) = "") Then
                    result = False
                    fw.FERR(fld) = True
                End If
            Next
        Else
            result = False
        End If
        If Not result Then fw.FERR("REQUIRED") = True
        Return result
    End Function
    'same as above but fields param passed as a qw string
    Public Overloads Function validateRequired(item As Hashtable, fields As String) As Boolean
        Return validateRequired(item, Utils.qw(fields))
    End Function

    ''' <summary>
    ''' Check validation result (validate_required)
    ''' </summary>
    ''' <param name="result">to use from external validation check</param>
    ''' <remarks>throw ValidationException exception if global ERR non-empty.
    ''' Also set global ERR[INVALID] if ERR non-empty, but ERR[REQUIRED] not true
    ''' </remarks>
    Public Sub validateCheckResult(Optional result As Boolean = True)
        If fw.FERR.ContainsKey("REQUIRED") AndAlso fw.FERR("REQUIRED") Then
            result = False
        End If

        If fw.FERR.Count > 0 AndAlso (Not fw.FERR.ContainsKey("REQUIRED") OrElse Not fw.FERR("REQUIRED")) Then
            fw.FERR("INVALID") = True
            result = False
        End If

        If Not result Then Throw New ValidationException()
    End Sub

    ''' <summary>
    ''' Set list sorting fields - Me.list_orderby according to Me.list_filter filter and Me.list_sortmap and Me.list_sortdef
    ''' </summary>
    ''' <remarks></remarks>
    Public Overridable Sub setListSorting()
        If Me.list_sortdef Is Nothing Then Throw New Exception("No default sort order defined, define in list_sortdef ")
        If Me.list_sortmap Is Nothing Then Throw New Exception("No sort order mapping defined, define in list_sortmap ")

        Dim sortdef_field As String = Nothing
        Dim sortdef_dir As String = Nothing
        Utils.split2(" ", Me.list_sortdef, sortdef_field, sortdef_dir)

        If Me.list_filter("sortby") = "" Then Me.list_filter("sortby") = sortdef_field
        If Me.list_filter("sortdir") <> "desc" AndAlso Me.list_filter("sortdir") <> "asc" Then Me.list_filter("sortdir") = sortdef_dir

        Dim orderby As String = Trim(Me.list_sortmap(Me.list_filter("sortby")))
        If Not orderby > "" Then Throw New Exception("No orderby defined for [" & Me.list_filter("sortby") & "], define in list_sortmap")

        If Me.list_filter("sortdir") = "desc" Then
            'if sortdir is desc, i.e. opposite to default - invert order for orderby fields
            'go thru each order field
            Dim aorderby As String() = Split(orderby, ",")
            For i As Integer = 0 To aorderby.Length - 1
                Dim field As String = Nothing, order As String = Nothing
                Utils.split2("\s+", Trim(aorderby(i)), field, order)

                If order = "desc" Then
                    order = "asc"
                Else
                    order = "desc"
                End If
                aorderby(i) = field & " " & order
            Next
            orderby = Join(aorderby, ", ")

        End If
        Me.list_orderby = orderby
    End Sub

    ''' <summary>
    ''' Add to Me.list_where search conditions from Me.list_filter("s") and based on fields in Me.search_fields
    ''' </summary>
    ''' <remarks>Sample: Me.search_fields="field1 field2,!field3 field4" => field1 LIKE '%$s%' or (field2 LIKE '%$s%' and field3='$s') or field4 LIKE '%$s%'</remarks>
    Public Overridable Sub setListSearch()
        Dim s As String = Trim(Me.list_filter("s"))
        If s > "" AndAlso Me.search_fields > "" Then
            Dim list_table_name As String = list_view
            If list_table_name = "" Then list_table_name = model0.table_name

            Dim like_quoted As String = db.q("%" & s & "%")

            Dim afields As String() = Utils.qw(Me.search_fields) 'OR fields delimited by space
            For i As Integer = 0 To afields.Length - 1
                Dim afieldsand As String() = Split(afields(i), ",") 'AND fields delimited by comma

                For j As Integer = 0 To afieldsand.Length - 1
                    Dim fand As String = afieldsand(j)
                    If fand.Substring(0, 1) = "!" Then
                        'exact match
                        fand = fand.Substring(1)
                        afieldsand(j) = fand & " = " & db.qone(list_table_name, fand, s)
                    Else
                        'like match
                        afieldsand(j) = fand & " LIKE " & like_quoted
                    End If
                Next
                afields(i) = Join(afieldsand, " and ")
            Next
            list_where &= " and (" & Join(afields, " or ") & ")"
        End If

        If list_filter("userlist") > "" Then
            Me.list_where &= " and id IN (select ti.item_id from " & fw.model(Of UserLists).table_items & " ti where ti.user_lists_id=" & db.qi(list_filter("userlist")) & " and ti.add_users_id=" & fw.model(Of Users).meId & " ) "
        End If

        If related_id > "" AndAlso related_field_name > "" Then
            list_where &= " and " & db.q_ident(related_field_name) & "=" & db.q(related_id)
        End If

        setListSearchAdvanced()
    End Sub

    ''' <summary>
    ''' set list_where based on search[] filter
    ''' </summary>
    Public Overridable Sub setListSearchAdvanced()
        'advanced search
        Dim hsearch = reqh("search")
        For Each fieldname In hsearch.Keys
            If hsearch(fieldname) > "" AndAlso (Not is_dynamic OrElse getViewListMap().ContainsKey(fieldname)) Then
                Me.list_where &= " and " & db.q_ident(fieldname) & " LIKE " & db.q("%" & hsearch(fieldname) & "%")
            End If
        Next
    End Sub

    ''' <summary>
    ''' set list_where filter based on status filter: 
    ''' - if status not set - filter our deleted (i.e. show all)
    ''' - if status set - filter by status, but if status=127 (deleted) only allow to see deleted by admins
    ''' </summary>
    Public Overridable Sub setListSearchStatus()
        If model0.field_status > "" Then
            If Me.list_filter("status") > "" Then
                Dim status = Utils.f2int(Me.list_filter("status"))
                'if want to see trashed and not admin - just show active
                If status = 127 And Not fw.model(Of Users).checkAccess(Users.ACL_ADMIN, False) Then status = 0
                Me.list_where &= " and " & db.q_ident(model0.field_status) & "=" & db.qi(status)
            Else
                Me.list_where &= " and " & db.q_ident(model0.field_status) & "<>127 " 'by default - show all non-deleted
            End If
        End If
    End Sub

    ''' <summary>
    ''' Perform 2 queries to get list of rows.
    ''' Set variables:
    ''' Me.list_count - count of rows obtained from db
    ''' Me.list_rows list of rows
    ''' Me.list_pager pager from FormUtils.get_pager
    ''' </summary>
    ''' <remarks></remarks>
    Public Overridable Sub getListRows()
        If list_view = "" Then list_view = model0.table_name
        Me.list_count = db.value("select count(*) from " & list_view & " where " & Me.list_where)
        If Me.list_count > 0 Then
            Dim offset As Integer = Me.list_filter("pagenum") * Me.list_filter("pagesize")
            Dim limit As Integer = Me.list_filter("pagesize")

            'for SQL Server 2012+
            Dim sql As String = "SELECT * FROM " & list_view &
                                " WHERE " & Me.list_where &
                                " ORDER BY " & Me.list_orderby &
                                " OFFSET " & offset & " ROWS " &
                                " FETCH NEXT " & limit & " ROWS ONLY"

            'for 2005<= SQL Server versions <2012
            'offset+1 because _RowNumber starts from 1
            'Dim sql As String = "SELECT * FROM (" &
            '                "   SELECT *, ROW_NUMBER() OVER (ORDER BY " & Me.list_orderby & ") AS _RowNumber" &
            '                "   FROM " & list_view &
            '                "   WHERE " & Me.list_where &
            '                ") tmp WHERE _RowNumber BETWEEN " & (offset + 1) & " AND " & (offset + 1 + limit - 1)

            'for MySQL this would be much simplier
            'sql = "SELECT * FROM model0.table_name WHERE Me.list_where ORDER BY Me.list_orderby LIMIT offset, limit";

            Me.list_rows = db.array(sql)
            Me.list_pager = FormUtils.getPager(Me.list_count, Me.list_filter("pagenum"), Me.list_filter("pagesize"))
        Else
            Me.list_rows = New ArrayList
            Me.list_pager = New ArrayList
        End If

        If related_id > "" Then
            Utils.arrayInject(list_rows, New Hashtable From {{"related_id", related_id}})
        End If

        'If related_field_name > "" Then
        '    For Each row As Hashtable In Me.list_rows
        '        row("related") = model_related.one(row(related_field_name))
        '    Next
        'End If

        'add/modify rows from db - use in override child class
        'For Each row As Hashtable In list_rows
        '    row("field") = "value"
        'Next

    End Sub

    Public Sub setFormError(ex As Exception)
        'if Validation exception - don't set general error message - specific validation message set in templates
        If Not (TypeOf ex Is ValidationException) Then
            fw.G("err_msg") = ex.Message
        End If
    End Sub

    ''' <summary>
    ''' Add or update records in db (Me.model0)
    ''' </summary>
    ''' <param name="id">id of the record, 0 if add</param>
    ''' <param name="fields">hash of field/values</param>
    ''' <returns>new autoincrement id (if added) or old id (if update)</returns>
    ''' <remarks>Also set fw.FLASH</remarks>
    Public Overridable Function modelAddOrUpdate(id As Integer, fields As Hashtable) As Integer
        If id > 0 Then
            model0.update(id, fields)
            fw.FLASH("record_updated", 1)
        Else
            id = model0.add(fields)
            fw.FLASH("record_added", 1)
        End If
        Return id
    End Function

    Public Overridable Function getReturnLocation(Optional id As String = "") As String
        Dim result = ""
        Dim url As String
        Dim url_q As String = IIf(related_id > "", "&related_id=" & related_id, "")
        Dim is_add_new = reqi("is_add_more")

        If id > "" Then
            If is_add_new = 1 Then
                'if Submit and Add New - redirect to new
                url = Me.base_url & "/new"
                url_q = "&copy_id=" & id
            Else
                'or just return to edit screen
                url = Me.base_url & "/" & id & "/edit"
            End If
        Else
            url = Me.base_url
        End If

        If url_q > "" Then
            url_q = Regex.Replace(url_q, "^\&", "") 'make url clean
            url_q = "?" & url_q
        End If

        If is_add_new <> 1 AndAlso return_url > "" Then
            If fw.isJsonExpected() Then
                'if json - it's usually autosave - don't redirect back to return url yet
                result = url & url_q & IIf(url_q > "", "&", "?") & "return_url=" & Utils.urlescape(return_url)
            Else
                result = return_url
            End If
        Else
            result = url & url_q
        End If

        Return result
    End Function

    ''' <summary>
    ''' Called from SaveAction/DeleteAction/DeleteMulti or similar. Return json or route redirect back to ShowForm or redirect to proper location
    ''' </summary>
    ''' <param name="success">operation successful or not</param>
    ''' <param name="id">item id</param>
    ''' <param name="is_new">true if it's newly added item</param>
    ''' <param name="action">route redirect to this method if error</param>
    ''' <param name="location">redirect to this location if success</param>
    ''' <param name="more_json">added to json response</param>
    ''' <returns></returns>
    Public Overridable Overloads Function saveCheckResult(success As Boolean, Optional id As String = "", Optional is_new As Boolean = False, Optional action As String = "ShowForm", Optional location As String = "", Optional more_json As Hashtable = Nothing) As Hashtable
        If location = "" Then location = Me.getReturnLocation(id)

        If fw.isJsonExpected() Then
            Dim ps = New Hashtable From {
                {"_json", True},
                {"success", success},
                {"id", id},
                {"is_new", is_new},
                {"location", location},
                {"err_msg", fw.G("err_msg")}
            }
            'TODO add ERR field errors to response if any
            If Not IsNothing(more_json) Then Utils.mergeHash(ps, more_json)

            Return ps
        Else
            'If save Then success - Return redirect
            'If save Then failed - Return back To add/edit form
            If success Then
                fw.redirect(location)
            Else
                fw.routeRedirect(action, New String() {id})
            End If
        End If
        Return Nothing
    End Function

    Public Overridable Overloads Function saveCheckResult(success As Boolean, more_json As Hashtable) As Hashtable
        Return saveCheckResult(success, "", False, "no_action", "", more_json)
    End Function


    '********************************** dynamic controller support
    Public Overridable Function getViewListMap() As Hashtable
        If view_list_map_cache Is Nothing Then view_list_map_cache = Utils.qh(view_list_map)
        Return view_list_map_cache
    End Function

    'as arraylist of hashtables {field_name=>, field_name_visible=> [, is_checked=>true]} in right order
    'if fields defined - show fields only
    'if is_all true - then show all fields (not only from fields param)
    Public Overridable Function getViewListArr(Optional fields As String = "", Optional is_all As Boolean = False) As ArrayList
        Dim result As New ArrayList

        'if fields defined - first show these fields, then the rest
        Dim fields_added As New Hashtable
        If fields > "" Then
            Dim map = getViewListMap()
            For Each fieldname In Utils.qw(fields)
                result.Add(New Hashtable From {{"field_name", fieldname}, {"field_name_visible", map(fieldname)}, {"is_checked", True}})
                fields_added(fieldname) = True
            Next
        End If

        If is_all Then
            'rest/all fields
            Dim arr = Utils.qw(view_list_map)
            For Each v In arr
                v = Replace(v, "&nbsp;", " ")
                Dim asub() As String = Split(v, "|", 2)
                If UBound(asub) < 1 Then Throw New ApplicationException("Wrong Format for view_list_map")
                If fields_added.ContainsKey(asub(0)) Then Continue For

                result.Add(New Hashtable From {{"field_name", asub(0)}, {"field_name_visible", asub(1)}})
            Next
        End If
        Return result
    End Function

    Public Overridable Function getViewListSortmap() As Hashtable
        Dim result As New Hashtable
        For Each fieldname In getViewListMap().Keys
            result(fieldname) = fieldname
        Next
        Return result
    End Function

    Public Overridable Function getViewListUserFields() As String
        Dim item = fw.model(Of UserViews).oneByScreen(base_url) 'base_url is screen identifier
        Return IIf(item("fields") > "", item("fields"), view_list_defaults)
    End Function

    'add to ps:
    ' headers
    ' headers_search
    ' depends on ps("list_rows")
    'usage:
    ' model.setViewList(ps, reqh("search"))
    Public Overridable Sub setViewList(ps As Hashtable, hsearch As Hashtable)
        Dim fields = getViewListUserFields()

        Dim headers = getViewListArr(fields)
        'add search from user's submit
        For Each header As Hashtable In headers
            header("search_value") = hsearch(header("field_name"))
        Next

        ps("headers") = headers
        ps("headers_search") = headers

        Dim hcustom = Utils.qh(view_list_custom)

        'dynamic cols
        For Each row As Hashtable In ps("list_rows")
            Dim cols As New ArrayList
            For Each fieldname In Utils.qw(fields)
                cols.Add(New Hashtable From {
                    {"row", row},
                    {"field_name", fieldname},
                    {"data", row(fieldname)},
                    {"is_custom", hcustom.ContainsKey(fieldname)}
                })
            Next
            row("cols") = cols
        Next
    End Sub

    '''''''''''''''''''''''''''''''''''''' Default Actions
    'Public Function IndexAction() As Hashtable
    '    logger("in Base controller IndexAction")
    '    Return New Hashtable
    'End Function

End Class

