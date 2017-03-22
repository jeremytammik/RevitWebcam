using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Security.Cryptography;

namespace RevitWebcam
{
  class GreyscaleBitmapData
  {
    //Bitmap _bitmap;

    /// <summary>
    /// Return greyscale intensity of given colour.
    /// To convert an RGB image to grayscale, you can use the standard 
    /// NTSC conversion formula that is used for calculating the effective 
    /// luminance of a pixel, cf.
    /// http://www.mathworks.com/support/solutions/en/data/1-1ASCU/index.html
    /// </summary>
    //static double Intensity( Color c )
    //{
    //  return 0.2989 * c.R + 0.5870 * c.G + 0.1140 * c.B;
    //}

    /*
     * http://www.codeproject.com/KB/GDI-plus/comparingimages.aspx?msg=2054241
     * 
    public static CompareResult Compare( Bitmap bmp1, Bitmap bmp2 )
    {
      CompareResult cr = CompareResult.ciCompareOk;

      //Test to see if we have the same size of image
      if( bmp1.Size != bmp2.Size )
      {
        cr = CompareResult.ciSizeMismatch;
      }
      else
      {
        //Sizes are the same so start comparing pixels
        for( int x = 0; x < bmp1.Width
             && cr == CompareResult.ciCompareOk; x++ )
        {
          for( int y = 0; y < bmp1.Height
                       && cr == CompareResult.ciCompareOk; y++ )
          {
            if( bmp1.GetPixel( x, y ) != bmp2.GetPixel( x, y ) )
              cr = CompareResult.ciPixelMismatch;
          }
        }
      }
      return cr;
    }

    public static CompareResult Compare( Bitmap bmp1, Bitmap bmp2 )
    {
      CompareResult cr = CompareResult.ciCompareOk;

      //Test to see if we have the same size of image
      if( bmp1.Size != bmp2.Size )
      {
        cr = CompareResult.ciSizeMismatch;
      }
      else
      {
        //Convert each image to a byte array
        System.Drawing.ImageConverter ic =
               new System.Drawing.ImageConverter();
        byte[] btImage1 = new byte[1];
        btImage1 = ( byte[] ) ic.ConvertTo( bmp1, btImage1.GetType() );
        byte[] btImage2 = new byte[1];
        btImage2 = ( byte[] ) ic.ConvertTo( bmp2, btImage2.GetType() );

        //Compute a hash for each image
        SHA256Managed shaM = new SHA256Managed();
        byte[] hash1 = shaM.ComputeHash( btImage1 );
        byte[] hash2 = shaM.ComputeHash( btImage2 );

        //Compare the hash values
        for( int i = 0; i < hash1.Length && i < hash2.Length
                          && cr == CompareResult.ciCompareOk; i++ )
        {
          if( hash1[i] != hash2[i] )
            cr = CompareResult.ciPixelMismatch;
        }
      }
      return cr;
    }
    */

    static Bitmap GetBitmap( int w, int h, string url )
    {
      using( WebClient client = new WebClient() )
      {
        byte[] data = client.DownloadData( url );

        using( Image img = Image.FromStream( new MemoryStream( data ) ) )
        {
          return new Bitmap(
            img.GetThumbnailImage( w, h, null, IntPtr.Zero ) );
        }
      }
    }

    static byte [] GetHashValue( Bitmap bitmap )
    {
      // convert image to a byte array

      ImageConverter ic = new ImageConverter();

      byte[] bytes = ( byte[] ) ic.ConvertTo( 
        bitmap, typeof( byte[] ) );

      // compute a hash for image

      SHA256Managed shaM = new SHA256Managed();
      return shaM.ComputeHash( bytes );
    }

    byte[] _hashValue;
    double[,] _brightness;

    public GreyscaleBitmapData( int w, int h, string url )
    {
      using( Bitmap bitmap = GetBitmap( w, h, url ) )
      {
        Debug.Assert( null != bitmap, "expected valid bitmap" );

        _hashValue = GetHashValue( bitmap );
        _brightness = new double[w, h];
        for( int x = 0; x < w; ++x )
        {
          for( int y = 0; y < h; ++y )
          {
            _brightness[x,y] = bitmap.GetPixel( x, h - (1 + y) ).GetBrightness();
          }
        }
      }
    }

    public double GetBrightnessAt( int x, int y )
    {
      return _brightness[x, y];
    }

    public byte [] HashValue
    {
      get
      {
        return _hashValue;
      }
    }
  }
}
