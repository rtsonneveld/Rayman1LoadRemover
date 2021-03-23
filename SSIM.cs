/*
 * ORIGINAL SOURCE: https://www.programmersought.com/article/44241923170/
 *
 * As a result SSIMResult ssim comprising three member variables:
 * diff: This is a contrast difference of two pictures, 32F, values ​​between -1 and 1
 * mssim: the difference value BGRA four channels 0-1, the close proximity of about 1
 * score: is the average difference value of the three channels BGR, the two images as the overall difference value
 */
using OpenCvSharp;

public class SSIM
{
    public class SSIMResult
    {
        /// <summary>
        /// The average difference value of the three channels BGR, the two images as the overall difference value
        /// </summary>
        public double Score
        {
            get { return (Mssim.Val0 + Mssim.Val1 + Mssim.Val2) / 3; }
        }

        /// <summary>
        /// the difference value BGRA four channels 0-1, the close proximity of about 1
        /// </summary>
        public Scalar Mssim;
        /// <summary>
        /// This is a contrast difference of two pictures, 32F, values ​​between -1 and 1
        /// </summary>
        public Mat Diff;
    }


    public static SSIMResult GetMssim(Mat i1, Mat i2)
    {
        const double C1 = 6.5025, C2 = 58.5225;
        /***************************** INITS **********************************/
        MatType d = MatType.CV_32F;

        Mat I1 = new Mat(), I2 = new Mat();
        i1.ConvertTo(I1, d); // cannot calculate on one byte large values
        i2.ConvertTo(I2, d);

        Mat I2_2 = I2.Mul(I2); // I2^2
        Mat I1_2 = I1.Mul(I1); // I1^2
        Mat I1_I2 = I1.Mul(I2); // I1 * I2

        /***********************PRELIMINARY COMPUTING ******************************/

        Mat mu1 = new Mat(), mu2 = new Mat(); //
        Cv2.GaussianBlur(I1, mu1, new OpenCvSharp.Size(11, 11), 1.5);
        Cv2.GaussianBlur(I2, mu2, new OpenCvSharp.Size(11, 11), 1.5);

        Mat mu1_2 = mu1.Mul(mu1);
        Mat mu2_2 = mu2.Mul(mu2);
        Mat mu1_mu2 = mu1.Mul(mu2);

        Mat sigma1_2 = new Mat(), sigma2_2 = new Mat(), sigma12 = new Mat();

        Cv2.GaussianBlur(I1_2, sigma1_2, new OpenCvSharp.Size(11, 11), 1.5);
        sigma1_2 -= mu1_2;

        Cv2.GaussianBlur(I2_2, sigma2_2, new OpenCvSharp.Size(11, 11), 1.5);
        sigma2_2 -= mu2_2;

        Cv2.GaussianBlur(I1_I2, sigma12, new OpenCvSharp.Size(11, 11), 1.5);
        sigma12 -= mu1_mu2;

        ///////////////////////////////// FORMULA ////////////////////////////////
        Mat t1, t2, t3;

        t1 = 2 * mu1_mu2 + C1;
        t2 = 2 * sigma12 + C2;
        t3 = t1.Mul(t2); // t3 = ((2*mu1_mu2 + C1).*(2*sigma12 + C2))

        t1 = mu1_2 + mu2_2 + C1;
        t2 = sigma1_2 + sigma2_2 + C2;
        t1 = t1.Mul(t2); // t1 =((mu1_2 + mu2_2 + C1).*(sigma1_2 + sigma2_2 + C2))

        Mat ssim_map = new Mat();
        Cv2.Divide(t3, t1, ssim_map); // ssim_map =  t3./t1;

        Scalar mssim = Cv2.Mean(ssim_map); // mssim = average of ssim map



        SSIMResult result = new SSIMResult();
        result.Diff = ssim_map;
        result.Mssim = mssim;


        return result;
    }
}