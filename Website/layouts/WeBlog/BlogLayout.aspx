﻿<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="BlogLayout.aspx.cs" Inherits="Sitecore.Modules.WeBlog.Layouts.BlogLayout" %>
<%@ Register TagPrefix="wb" Namespace="Sitecore.Modules.WeBlog.WebControls" Assembly="Sitecore.Modules.WeBlog" %>

<!DOCTYPE html PUBLIC "-//W3C//DTD XHTML 1.0 Transitional//EN" "http://www.w3.org/TR/xhtml1/DTD/xhtml1-transitional.dtd">

<html lang="en" xml:lang="en" xmlns="http://www.w3.org/1999/xhtml">
  <head runat="server">
    <title><%= GetItemTitle() %></title>
    <wb:Syndication runat="server" />
    <wb:RsdIncludes runat="server" />
    <link href="/sitecore modules/Blog/Includes/Common.css" rel="stylesheet" />
    <wb:ThemeIncludes runat="server" />
    <script src="http://ajax.googleapis.com/ajax/libs/jquery/1.4.3/jquery.min.js" type="text/javascript"></script>
    <script src="/sitecore modules/Blog/Includes/jquery.url.js" type="text/javascript"></script>
    <script src="/sitecore modules/Blog/Includes/functions.js" type="text/javascript"></script>
  </head>
  <body>
  <form method="post" runat="server" id="mainform">
    <sc:placeholder ID="phContent" key="phContent" runat="server" />
  
  </form>
  </body>
</html>
