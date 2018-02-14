﻿using System;
using System.IO;
using System.Linq;
using AtlusScriptLib.Common.Registry;

namespace AtlusScriptLib.MessageScriptLanguage.Decompiler
{
    public sealed class MessageScriptDecompiler : IDisposable
    {
        private readonly TextWriter mWriter;

        public Library Library { get; set; }

        public bool OmitUnusedFunctions { get; set; } = true;

        public MessageScriptDecompiler( TextWriter writer )
        {
            mWriter = writer;
        }

        public void Decompile( MessageScript script )
        {
            foreach ( var message in script.Windows )
            {
                Decompile( message );
                mWriter.WriteLine();
            }
        }

        public void Decompile( IWindow message )
        {
            switch ( message.Type )
            {
                case WindowType.Dialogue:
                    Decompile( ( DialogWindow )message );
                    break;
                case WindowType.Selection:
                    Decompile( ( SelectionWindow )message );
                    break;

                default:
                    throw new NotImplementedException( message.Type.ToString() );
            }
        }

        public void Decompile( DialogWindow message )
        {
            if ( message.Speaker != null )
            {
                switch ( message.Speaker.Type )
                {
                    case SpeakerType.Named:
                        {
                            WriteOpenTag( "dlg" );
                            WriteTagArgument( message.Identifier );
                            {
                                mWriter.Write( " " );

                                var speaker = ( NamedSpeaker )message.Speaker;
                                if ( speaker.Name != null )
                                {
                                    WriteOpenTag();
                                    Decompile( speaker.Name, false );
                                    WriteCloseTag();
                                }
                            }
                            WriteCloseTag();
                        }
                        break;

                    case SpeakerType.Variable:
                        {
                            WriteOpenTag( "dlg" );
                            WriteTagArgument( message.Identifier );
                            {
                                mWriter.Write( " " );
                                WriteOpenTag();
                                mWriter.Write( ( ( VariableSpeaker )message.Speaker ).Index.ToString() );
                                WriteCloseTag();
                            }
                            WriteCloseTag();
                        }
                        break;
                }
            }
            else
            {
                WriteTag( "dlg", message.Identifier );
            }

            mWriter.WriteLine();

            foreach ( var line in message.Lines )
            {
                Decompile( line );
                mWriter.WriteLine();
            }
        }

        public void Decompile( SelectionWindow message )
        {
            WriteTag( "sel", message.Identifier );
            mWriter.WriteLine();

            foreach ( var line in message.Lines )
            {
                Decompile( line );
                mWriter.WriteLine();
            }
        }

        public void Decompile( TokenText line, bool emitLineEndTag = true )
        {
            foreach ( var token in line.Tokens )
            {
                Decompile( token );
            }

            if ( emitLineEndTag )
                WriteTag( "e" );
        }

        public void Decompile( IToken token )
        {
            switch ( token.Kind )
            {
                case TokenKind.String:
                    Decompile( ( StringToken )token );
                    break;
                case TokenKind.Function:
                    Decompile( ( FunctionToken )token );
                    break;
                case TokenKind.CodePoint:
                    Decompile( ( CodePointToken )token );
                    break;
                case TokenKind.NewLine:
                    Decompile( ( NewLineToken )token );
                    break;

                default:
                    throw new NotImplementedException( token.Kind.ToString() );
            }
        }

        public void Decompile( FunctionToken token )
        {
            if ( Library != null )
            {
                var library = Library.MessageScriptLibraries.FirstOrDefault( x => x.Index == token.FunctionTableIndex );
                if ( library != null )
                {
                    var function = library.Functions.FirstOrDefault( x => x.Index == token.FunctionIndex );
                    if ( function != null )
                    {
                        if ( function.Name == "@Unused" && OmitUnusedFunctions )
                            return;

                        if ( !string.IsNullOrWhiteSpace( function.Name ) )
                        {
                            WriteOpenTag( function.Name );

                            for ( var i = 0; i < function.Parameters.Count; i++ )
                            {
                                var argument = function.Parameters[i];
                                WriteTagArgument( token.Arguments[i].ToString() );
                            }

                            WriteCloseTag();
                            return;
                        }
                    }
                }
            }

            if ( token.Arguments.Count == 0 )
            {
                WriteTag( "f", token.FunctionTableIndex.ToString(), token.FunctionIndex.ToString() );
            }
            else
            {
                WriteOpenTag( "f" );
                WriteTagArgument( token.FunctionTableIndex.ToString() );
                WriteTagArgument( token.FunctionIndex.ToString() );

                foreach ( var tokenArgument in token.Arguments )
                {
                    WriteTagArgument( tokenArgument.ToString() );
                }

                WriteCloseTag();
            }
        }

        public void Decompile( StringToken token )
        {
            mWriter.Write( token.Value );
        }

        public void Decompile( CodePointToken token )
        {
            WriteTag( $"x 0x{token.HighSurrogate:X2} 0x{token.LowSurrogate:X2}" );
        }

        public void Decompile( NewLineToken token )
        {
            WriteTag( "n" );
        }

        public void Dispose()
        {
            mWriter.Dispose();
        }

        private void WriteOpenTag()
        {
            mWriter.Write( "[" );
        }

        private void WriteOpenTag( string tag )
        {
            mWriter.Write( $"[{tag}" );
        }

        private void WriteTagArgument( string argument )
        {
            mWriter.Write( " " );
            mWriter.Write( argument );
        }

        private void WriteCloseTag()
        {
            mWriter.Write( "]" );
        }

        private void WriteTag( string tag, params string[] arguments )
        {
            WriteOpenTag( tag );

            if ( arguments.Length != 0 )
            {
                foreach ( var argument in arguments )
                {
                    WriteTagArgument( argument );
                }
            }

            WriteCloseTag();
        }
    }
}